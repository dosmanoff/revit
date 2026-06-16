using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Domain;

/// <summary>
/// Resolves the two supported representations into one <see cref="StairAssembly"/> model:
/// native Revit <see cref="Stairs"/> (one assembly each, from its runs + landings), and
/// floor-modelled stairs (all picked floors form one assembly; sloped floors = flights,
/// flat floors = landings). The rebar engine resolves geometry through the same path so the
/// dump and the generated bars always agree.
/// </summary>
public static class StairSourceResolver
{
    private const double LandingSlopeTol = 0.03;   // ≈1.7° — below this a face is "flat"
    private const double DefaultWaistFt = 0.5;     // 6" fallback when the type has no waist param

    public static List<StairAssembly> Resolve(Document doc, IEnumerable<Element> picked)
    {
        var result = new List<StairAssembly>();
        var floors = new List<Floor>();

        foreach (Element e in picked)
        {
            if (e is Stairs st) result.Add(BuildFromStairs(doc, st));
            else if (e is Floor fl) floors.Add(fl);
        }

        if (floors.Count > 0) result.Add(BuildFromFloors(doc, floors));

        foreach (StairAssembly asm in result)
            try { StairContext.Populate(doc, asm); }
            catch (Exception ex) { asm.Warnings.Add($"Context: {ex.Message}"); }

        return result;
    }

    // ── Native Stairs ────────────────────────────────────────────────────────

    private static StairAssembly BuildFromStairs(Document doc, Stairs stairs)
    {
        var asm = new StairAssembly
        {
            Id = stairs.Id,
            Source = StairSourceKind.NativeStairs,
            HostElement = stairs,
            Mark = RevitGeom.Mark(stairs),
            Comments = RevitGeom.Comments(stairs),
        };

        bool hostOk = RevitGeom.IsValidRebarHost(stairs);
        if (!hostOk)
            asm.Warnings.Add("Native Stairs is not a valid rebar host in this model — bars may need a structural host.");

        int fi = 0;
        foreach (ElementId rid in stairs.GetStairsRuns())
        {
            if (doc.GetElement(rid) is not StairsRun run) continue;
            try { asm.Flights.Add(BuildFlightFromRun(doc, run, stairs, hostOk, fi++)); }
            catch (Exception ex) { asm.Warnings.Add($"Run {rid.Value}: {ex.Message}"); }
        }

        int li = 0;
        foreach (ElementId lid in stairs.GetStairsLandings())
        {
            if (doc.GetElement(lid) is not StairsLanding landing) continue;
            try { asm.Landings.Add(BuildLandingFromLanding(landing, stairs, hostOk, li++)); }
            catch (Exception ex) { asm.Warnings.Add($"Landing {lid.Value}: {ex.Message}"); }
        }

        return asm;
    }

    private static FlightComponent BuildFlightFromRun(
        Document doc, StairsRun run, Stairs stairs, bool hostOk, int index)
    {
        var path = new List<Curve>();
        foreach (Curve c in run.GetStairsPath()) path.Add(c);
        if (path.Count == 0) throw new InvalidOperationException("empty stairs path");

        XYZ start = path[0].GetEndPoint(0);
        XYZ end = path[^1].GetEndPoint(1);
        Pt2 runVec = RevitGeom.P2(end) - RevitGeom.P2(start);
        double horizRun = runVec.Length;
        Pt2 runDir = runVec.Normalized();

        double rise = run.Height;
        double slope = Math.Atan2(rise, Math.Max(1e-6, horizRun));

        Bounds3 bounds = RevitGeom.ElemBounds(run);
        double soffitZ = bounds.IsEmpty ? start.Z : bounds.Min.Z;
        var frame = FlightFrame.Create(new Pt3(start.X, start.Y, soffitZ), runDir, slope);

        int risers = SafeInt(() => run.ActualRisersNumber);
        int treads = SafeInt(() => run.ActualTreadsNumber);
        double waist = WaistOfRun(doc, run) ?? DefaultWaistFt;

        return new FlightComponent
        {
            Index = index,
            ComponentId = run.Id,
            Host = stairs,
            RebarHostOk = hostOk,
            SourceKind = "run",
            Frame = frame,
            WaistFt = waist,
            WidthFt = run.ActualRunWidth,
            HorizRunFt = horizRun,
            SlopeLengthFt = Math.Sqrt(horizRun * horizRun + rise * rise),
            TotalRiseFt = rise,
            SlopeRad = slope,
            RiserCount = risers,
            TreadCount = treads,
            TreadFt = treads > 0 ? horizRun / treads : 0,
            RiserFt = risers > 0 ? rise / risers : 0,
            Bounds = bounds,
        };
    }

    private static LandingComponent BuildLandingFromLanding(
        StairsLanding landing, Stairs stairs, bool hostOk, int index)
    {
        var loop = RevitGeom.ToPlanLoop(landing.GetFootprintBoundary());
        Bounds3 bounds = RevitGeom.ElemBounds(landing);

        return new LandingComponent
        {
            Index = index,
            ComponentId = landing.Id,
            Host = stairs,
            RebarHostOk = hostOk,
            SourceKind = "landing",
            ThicknessFt = landing.Thickness,
            ElevationFt = bounds.IsEmpty ? 0 : bounds.Max.Z,
            AreaSf = PlanBasis.Area(loop),
            Basis = PlanBasis.FromLongestEdge(loop),
            Boundary = loop,
            Bounds = bounds,
        };
    }

    private static double? WaistOfRun(Document doc, StairsRun run)
    {
        if (doc.GetElement(run.GetTypeId()) is Element rt)
            return RevitGeom.LookupLengthFt(rt,
                "Monolithic Run Structural Depth", "Structural Depth", "Waist", "Waist Depth");
        return null;
    }

    // ── Floor-modelled ───────────────────────────────────────────────────────

    private static StairAssembly BuildFromFloors(Document doc, List<Floor> floors)
    {
        Floor first = floors[0];
        var asm = new StairAssembly
        {
            Id = first.Id,
            Source = StairSourceKind.Floors,
            HostElement = first,
            Mark = RevitGeom.Mark(first),
            Comments = RevitGeom.Comments(first),
        };

        int fi = 0, li = 0;
        foreach (Floor floor in floors)
        {
            try
            {
                Solid? solid = RevitGeom.LargestSolid(floor);
                PlanarFace? top = solid is null ? null : RevitGeom.ExtremeFace(solid, top: true);
                XYZ n = top?.FaceNormal ?? XYZ.BasisZ;
                double slope = Math.Acos(Math.Clamp(Math.Abs(n.Z), -1.0, 1.0));

                if (slope < LandingSlopeTol)
                    asm.Landings.Add(BuildLandingFromFloor(doc, floor, li++));
                else
                    asm.Flights.Add(BuildFlightFromFloor(floor, n, slope, fi++));
            }
            catch (Exception ex) { asm.Warnings.Add($"Floor {floor.Id.Value}: {ex.Message}"); }
        }

        return asm;
    }

    private static FlightComponent BuildFlightFromFloor(Floor floor, XYZ topNormal, double slope, int index)
    {
        // Steepest-ascent horizontal direction of the top plane: -(nx, ny).
        Pt2 runDir = new Pt2(-topNormal.X, -topNormal.Y).Normalized();
        if (runDir.Length < 1e-9) runDir = new Pt2(1, 0);
        Pt2 widthDir = runDir.Perp();

        Bounds3 bounds = RevitGeom.ElemBounds(floor);
        List<Pt2> loop = LargestFloorLoop(floor);

        // Project loop points onto run / width axes to get extents and the lower-end origin.
        double uMin = double.MaxValue, uMax = double.MinValue, wMin = double.MaxValue, wMax = double.MinValue;
        foreach (Pt2 p in loop)
        {
            double u = p.Dot(runDir), w = p.Dot(widthDir);
            uMin = Math.Min(uMin, u); uMax = Math.Max(uMax, u);
            wMin = Math.Min(wMin, w); wMax = Math.Max(wMax, w);
        }
        double horizRun = loop.Count > 0 ? uMax - uMin : (bounds.IsEmpty ? 0 : bounds.Size.X);
        double width = loop.Count > 0 ? wMax - wMin : (bounds.IsEmpty ? 0 : bounds.Size.Y);
        double rise = horizRun * Math.Tan(slope);

        // Origin: lower end along run, mid width, at soffit z.
        double wMid = (wMin + wMax) / 2;
        Pt2 originPlan = runDir * uMin + widthDir * wMid;
        double soffitZ = bounds.IsEmpty ? 0 : bounds.Min.Z;
        var frame = FlightFrame.Create(new Pt3(originPlan.X, originPlan.Y, soffitZ), runDir, slope);

        return new FlightComponent
        {
            Index = index,
            ComponentId = floor.Id,
            Host = floor,
            RebarHostOk = RevitGeom.IsValidRebarHost(floor),
            SourceKind = "floor",
            Frame = frame,
            WaistFt = RevitGeom.FloorThicknessFt(floor),
            WidthFt = width,
            HorizRunFt = horizRun,
            SlopeLengthFt = Math.Sqrt(horizRun * horizRun + rise * rise),
            TotalRiseFt = rise,
            SlopeRad = slope,
            Bounds = bounds,
        };
    }

    private static LandingComponent BuildLandingFromFloor(Document doc, Floor floor, int index)
    {
        List<Pt2> loop = LargestFloorLoop(floor);
        Bounds3 bounds = RevitGeom.ElemBounds(floor);

        return new LandingComponent
        {
            Index = index,
            ComponentId = floor.Id,
            Host = floor,
            RebarHostOk = RevitGeom.IsValidRebarHost(floor),
            SourceKind = "floor",
            ThicknessFt = RevitGeom.FloorThicknessFt(floor),
            ElevationFt = bounds.IsEmpty ? 0 : bounds.Max.Z,
            AreaSf = PlanBasis.Area(loop),
            Basis = PlanBasis.FromLongestEdge(loop),
            Boundary = loop,
            Bounds = bounds,
        };
    }

    /// <summary>Largest plan loop of a floor — from its sketch profile, else its top face edges.</summary>
    private static List<Pt2> LargestFloorLoop(Floor floor)
    {
        Document doc = floor.Document;
        var loops = new List<List<Pt2>>();

        if (doc.GetElement(floor.SketchId) is Sketch sketch)
            foreach (CurveArray ca in sketch.Profile)
            {
                var pts = new List<Pt2>();
                foreach (Curve c in ca)
                    foreach (XYZ p in c.Tessellate())
                    {
                        Pt2 q = RevitGeom.P2(p);
                        if (pts.Count == 0 || (pts[^1] - q).Length > 1e-7) pts.Add(q);
                    }
                if (pts.Count >= 3) loops.Add(pts);
            }

        if (loops.Count == 0 && RevitGeom.LargestSolid(floor) is Solid solid &&
            RevitGeom.ExtremeFace(solid, top: true) is PlanarFace tf)
        {
            foreach (EdgeArray ea in tf.EdgeLoops)
            {
                var curves = new List<Curve>();
                foreach (Edge e in ea) curves.Add(e.AsCurve());
                var pts = RevitGeom.ToPlanLoop(curves);
                if (pts.Count >= 3) loops.Add(pts);
            }
        }

        return loops.Count == 0 ? new List<Pt2>() : loops.OrderByDescending(PlanBasis.Area).First();
    }

    private static int SafeInt(Func<int> f) { try { return f(); } catch { return 0; } }
}
