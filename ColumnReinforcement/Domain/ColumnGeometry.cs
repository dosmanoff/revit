using Autodesk.Revit.DB;

namespace ColumnReinforcement.Domain;

/// <summary>Cross-section shape of a column.</summary>
public enum ColumnSection
{
    Rectangular,
    Round,
}

/// <summary>
/// Local frame and overall sizes of a vertical structural column, expressed in
/// Revit's internal units (feet). Slanted columns are out of scope for Phase 1
/// — the selection filter excludes them, and <see cref="For"/> assumes vertical.
///
/// <para>Dimensions are recovered from the column's bottom face. For rectangular
/// columns rotated in plan this gives the true in-plan width/depth (projection
/// onto local X/Y), unlike the world-AABB shortcut. For round columns it gives
/// the true diameter as both width and depth (the AABB shortcut already gave
/// this number, but section detection requires the face analysis anyway).</para>
/// </summary>
public class ColumnGeometry
{
    public required FamilyInstance Instance { get; init; }

    /// <summary>Cross-section shape: rectangular/square or round.</summary>
    public required ColumnSection Section { get; init; }

    /// <summary>Centre of the column's base section, in world coordinates (feet).</summary>
    public required XYZ BaseCenter { get; init; }

    /// <summary>Unit vector along the column's local X axis (in-plan), accounting for rotation.</summary>
    public required XYZ LocalX { get; init; }

    /// <summary>Unit vector along the column's local Y axis (in-plan), accounting for rotation.</summary>
    public required XYZ LocalY { get; init; }

    /// <summary>Cross-section size along <see cref="LocalX"/>, in feet. For round, equals diameter.</summary>
    public required double Width { get; init; }

    /// <summary>Cross-section size along <see cref="LocalY"/>, in feet. For round, equals diameter.</summary>
    public required double Depth { get; init; }

    /// <summary>Top-of-column elevation minus base elevation, in feet (world Z extent of the AABB).</summary>
    public required double Height { get; init; }

    /// <summary>Diameter of the cross-section for <see cref="ColumnSection.Round"/> columns.</summary>
    public double Diameter => Width;

    public static ColumnGeometry For(FamilyInstance inst)
    {
        if (inst.IsSlantedColumn)
            throw new InvalidOperationException(
                $"Column {inst.Id.Value} is slanted; only vertical columns are supported.");

        Transform tr = inst.GetTotalTransform();
        BoundingBoxXYZ bb = inst.get_BoundingBox(null)
            ?? throw new InvalidOperationException(
                $"Column {inst.Id.Value} has no bounding box (view-dependent geometry only).");

        double height = bb.Max.Z - bb.Min.Z;

        // Pull the cross-section size and shape from the column's actual bottom face,
        // not the world AABB — for rotated rectangular columns the AABB is bigger than
        // the rectangle itself, which would offset the cage outside the concrete.
        var (width, depth, section) = AnalyzeCrossSection(inst, tr) ?? FallbackToAabb(bb, tr);

        XYZ localX = tr.BasisX.Normalize();
        XYZ localY = tr.BasisY.Normalize();

        // Canonicalise so LocalX is always the SHORTER in-plan side and Width ≤ Depth,
        // independent of how the family authored its local axes. This makes the bar
        // enumeration deterministic: LongBarsW (the "width" count) always lands on the
        // short faces, LongBarsD on the long faces — matching the convention the user
        // fills the CSV with ("bars along the short edge" = W). Rotating the frame +90°
        // about Z (newX = oldY, newY = -oldX) keeps it right-handed, so tie winding and
        // hook orientation are unaffected.
        if (section == ColumnSection.Rectangular && width > depth)
        {
            (width, depth) = (depth, width);
            XYZ newX = localY;
            XYZ newY = localX.Negate();
            localX = newX;
            localY = newY;
        }

        return new ColumnGeometry
        {
            Instance   = inst,
            Section    = section,
            BaseCenter = new XYZ(tr.Origin.X, tr.Origin.Y, bb.Min.Z),
            LocalX     = localX,
            LocalY     = localY,
            Width      = width,
            Depth      = depth,
            Height     = height,
        };
    }

    /// <summary>
    /// World-coordinate point at local (x, y) inside the column's cross-section, at height
    /// <paramref name="z"/> above the column's base. <paramref name="x"/>/<paramref name="y"/>
    /// are measured from <see cref="BaseCenter"/> along <see cref="LocalX"/>/<see cref="LocalY"/>.
    /// </summary>
    public XYZ At(double x, double y, double z) =>
        BaseCenter + LocalX * x + LocalY * y + XYZ.BasisZ * z;

    /// <summary>
    /// Unit vector pointing from a bar at local <c>(x, y)</c> inward into the column section.
    /// Round: radially toward the centre. Rectangular: perpendicular to the nearest face
    /// (LocalX-axis when the bar is on a left/right face, LocalY-axis on top/bottom; the
    /// tiebreaker for corner bars prefers LocalX).
    /// </summary>
    public XYZ InwardDirection(double x, double y)
    {
        if (Section == ColumnSection.Round)
        {
            XYZ radial = LocalX * x + LocalY * y;
            if (radial.GetLength() < 1e-9) return LocalX;
            return -radial.Normalize();
        }
        return Math.Abs(x) >= Math.Abs(y)
            ? LocalX * -Math.Sign(x == 0 ? 1 : x)
            : LocalY * -Math.Sign(y == 0 ? 1 : y);
    }

    /// <summary>
    /// Normal vector for a bar whose bend plane contains <see cref="InwardDirection"/> and
    /// world Z. Used as the <c>norm</c> argument to <c>Rebar.CreateFromCurves</c> for L-form
    /// dowels and bent upper splices.
    /// </summary>
    public XYZ NormalForBendAt(double x, double y) =>
        XYZ.BasisZ.CrossProduct(InwardDirection(x, y)).Normalize();

    // ─────────────────────────────────────────────────────────────────────────

    private static (double width, double depth, ColumnSection section)? AnalyzeCrossSection(
        FamilyInstance inst, Transform tr)
    {
        Solid? solid = GetMainSolid(inst);
        if (solid is null) return null;

        PlanarFace? bottom = FindBottomFace(solid);
        if (bottom is null) return null;

        EdgeArrayArray loops = bottom.EdgeLoops;
        if (loops.Size < 1) return null;

        EdgeArray outer = loops.get_Item(0);

        bool allArcs = true;
        bool anyLine = false;
        for (int i = 0; i < outer.Size; i++)
        {
            Curve c = outer.get_Item(i).AsCurve();
            if (c is Line) { anyLine = true; allArcs = false; }
            else if (c is not Arc) { allArcs = false; }
        }
        ColumnSection section = (allArcs && !anyLine) ? ColumnSection.Round : ColumnSection.Rectangular;

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        for (int i = 0; i < outer.Size; i++)
        {
            foreach (XYZ pt in outer.get_Item(i).Tessellate())
            {
                XYZ rel = pt - tr.Origin;
                double x = rel.DotProduct(tr.BasisX);
                double y = rel.DotProduct(tr.BasisY);
                if (x < xMin) xMin = x;
                if (x > xMax) xMax = x;
                if (y < yMin) yMin = y;
                if (y > yMax) yMax = y;
            }
        }

        double width = xMax - xMin;
        double depth = yMax - yMin;
        if (width <= 0 || depth <= 0) return null;

        return (width, depth, section);
    }

    private static Solid? GetMainSolid(FamilyInstance inst)
    {
        var opts = new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
            DetailLevel = ViewDetailLevel.Fine,
        };

        GeometryElement geo = inst.get_Geometry(opts);
        if (geo is null) return null;

        Solid? best = null;
        double bestVolume = 0;

        foreach (GeometryObject obj in geo)
        {
            switch (obj)
            {
                case Solid s when s.Volume > bestVolume:
                    best = s; bestVolume = s.Volume; break;

                case GeometryInstance gi:
                    foreach (GeometryObject inner in gi.GetInstanceGeometry())
                        if (inner is Solid si && si.Volume > bestVolume)
                        { best = si; bestVolume = si.Volume; }
                    break;
            }
        }
        return best;
    }

    private static PlanarFace? FindBottomFace(Solid solid)
    {
        PlanarFace? best = null;
        double lowestZ = double.MaxValue;
        foreach (Face f in solid.Faces)
        {
            if (f is not PlanarFace pf) continue;
            // Face normal pointing roughly straight down = bottom face of the solid.
            if (pf.FaceNormal.DotProduct(XYZ.BasisZ) > -0.99) continue;

            if (pf.Origin.Z < lowestZ)
            {
                lowestZ = pf.Origin.Z;
                best = pf;
            }
        }
        return best;
    }

    private static (double width, double depth, ColumnSection section) FallbackToAabb(
        BoundingBoxXYZ bb, Transform tr)
    {
        // Project AABB corners — accurate at zero rotation, an overestimate otherwise.
        // Used only when bottom-face analysis fails (no solid, no planar bottom face).
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (XYZ corner in EnumerateCorners(bb))
        {
            XYZ rel = corner - tr.Origin;
            double x = rel.DotProduct(tr.BasisX);
            double y = rel.DotProduct(tr.BasisY);
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return (maxX - minX, maxY - minY, ColumnSection.Rectangular);
    }

    private static IEnumerable<XYZ> EnumerateCorners(BoundingBoxXYZ bb)
    {
        XYZ mn = bb.Min, mx = bb.Max;
        yield return new XYZ(mn.X, mn.Y, mn.Z);
        yield return new XYZ(mx.X, mn.Y, mn.Z);
        yield return new XYZ(mn.X, mx.Y, mn.Z);
        yield return new XYZ(mx.X, mx.Y, mn.Z);
        yield return new XYZ(mn.X, mn.Y, mx.Z);
        yield return new XYZ(mx.X, mn.Y, mx.Z);
        yield return new XYZ(mn.X, mx.Y, mx.Z);
        yield return new XYZ(mx.X, mx.Y, mx.Z);
    }
}
