using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Places starter bars (dowels) extending from an element BELOW the current
/// column up into its cage. One dowel per longitudinal-bar position.
///
/// <para>Originally built for foundation slabs (PR-09), the builder now also
/// supports lower columns and beams as hosts (PR-36):
/// <list type="bullet">
///   <item><b>Foundation</b>/<b>Floor</b> — vertical drop into the slab
///         (embedment = slab depth). L-form bends the bottom into the slab.</item>
///   <item><b>Column</b> — vertical lap inside the lower column, then up into
///         the current column for the lap with its cage. Bar type matches
///         the CURRENT cage (the upper column's longitudinal). Each dowel
///         sits 1·d_bar offset along the column face from its lap-pair so
///         the lap is non-contact per ACI 318 §25.5.1.3.</item>
///   <item><b>Beam</b> — like Foundation, but with the beam's bounding box
///         as the host. Used for columns landing on transfer beams.</item>
/// </list>
/// </para>
/// </summary>
public class FoundationDowelBuilder
{
    private readonly Document _doc;

    public FoundationDowelBuilder(Document doc) => _doc = doc;

    public record struct Result(int Created, string? SkipReason);

    public Result Build(
        ColumnGeometry geom, ColumnReinforcementConfig cfg, string tag,
        (Element host, DowelHost kind)? resolvedHost)
    {
        DowelsConfig d = cfg.Dowels;
        if (!d.Enabled) return new Result(0, null);
        if (resolvedHost is null)
            return new Result(0, NoHostMessage(d));

        Element host = resolvedHost.Value.host;
        DowelHost kind = resolvedHost.Value.kind;

        BoundingBoxXYZ hostBb = host.get_BoundingBox(null)
            ?? throw new InvalidOperationException(
                $"Host element {host.Id.Value} below column {geom.Instance.Id.Value} has no bounding box.");

        return kind switch
        {
            DowelHost.Foundation or DowelHost.Floor => BuildSlabHosted(geom, cfg, tag, hostBb),
            DowelHost.Beam                          => BuildBeamHosted(geom, cfg, tag, hostBb),
            DowelHost.Column                        => BuildColumnHosted(geom, cfg, tag, hostBb),
            _ => throw new InvalidOperationException($"Unsupported dowel host kind: {kind}"),
        };
    }

    private static string NoHostMessage(DowelsConfig d)
    {
        string searched = d.Host switch
        {
            DowelHost.Foundation => "Structural Foundation",
            DowelHost.Floor      => "Structural Floor",
            DowelHost.Column     => "Structural Column",
            DowelHost.Beam       => "Structural Framing (beam)",
            _ => d.OnlyStructuralFoundation ? "Structural Foundation" : "Structural Foundation or Floor",
        };
        return $"Dowels enabled but no {searched} found directly below the column. " +
               (d.Host == DowelHost.Auto && d.OnlyStructuralFoundation
                   ? "Either re-categorise the foundation, set dowels.onlyStructuralFoundation=false to also search Floors, " +
                     "or set dowels.host=Column/Beam if the column lands on one of those."
                   : "Check that the host element's plan extent includes the column centreline.");
    }

    // ── Slab and beam: vertical drop into the host depth ────────────────

    private Result BuildSlabHosted(ColumnGeometry geom, ColumnReinforcementConfig cfg, string tag, BoundingBoxXYZ hostBb)
    {
        DowelsConfig d = cfg.Dowels;
        RebarBarType barType = RebarFactory.GetBarType(_doc, d.BarType);
        RebarHookType? hookTop    = RebarFactory.GetHookType(_doc, d.HookTopType);
        RebarHookType? hookBottom = RebarFactory.GetHookType(_doc, d.HookBottomType);
        RebarShape?    dowelShape = RebarFactory.GetRebarShape(_doc, d.Shape);

        double extension = cfg.Ft(d.Extension);
        double embedment = cfg.Ft(d.Embedment);
        double legLength = cfg.Ft(d.LegLength);

        if (extension <= 0) throw new InvalidOperationException("Dowel extension must be positive.");
        if (embedment <= 0) throw new InvalidOperationException("Dowel embedment must be positive.");
        if (d.Form == DowelForm.L && legLength <= 0)
            throw new InvalidOperationException("L-form dowel leg length must be positive.");

        double hostTopZ = hostBb.Max.Z;
        double hostBottomZ = hostBb.Min.Z;
        double hostThickness = hostTopZ - hostBottomZ;
        if (embedment > hostThickness)
            throw new InvalidOperationException(
                $"Dowel embedment ({UnitConv.FtToIn(embedment):0.##}\") exceeds host thickness " +
                $"({UnitConv.FtToIn(hostThickness):0.##}\").");

        var positions = LongitudinalBarBuilder.ComputeCagePositions(_doc, cfg, geom);
        bool[] mask = LongitudinalBarBuilder.ResolvePositionMask(positions, d.Positions);

        double zLocalHostTop     = hostTopZ - geom.BaseCenter.Z;
        double zLocalDowelBottom = zLocalHostTop - embedment;
        double zLocalDowelTop    = zLocalHostTop + extension;

        int created = 0;
        for (int i = 0; i < positions.Count; i++)
        {
            if (!mask[i]) continue;
            (double x, double y) = positions[i];
            IList<Curve> curves = d.Form switch
            {
                DowelForm.Straight => Straight(geom, x, y, zLocalDowelBottom, zLocalDowelTop),
                DowelForm.L        => LShape (geom, x, y, zLocalDowelBottom, zLocalDowelTop, legLength),
                _ => throw new InvalidOperationException($"Unknown dowel form: {d.Form}"),
            };

            XYZ normal = d.Form == DowelForm.Straight ? geom.LocalX : geom.NormalForBendAt(x, y);

            RebarFactory.Create(
                _doc, RebarStyle.Standard, barType, geom.Instance, normal, curves, tag,
                startHook: hookBottom, endHook: hookTop, shape: dowelShape);
            created++;
        }

        return new Result(created, null);
    }

    private Result BuildBeamHosted(ColumnGeometry geom, ColumnReinforcementConfig cfg, string tag, BoundingBoxXYZ hostBb)
    {
        // Geometrically identical to slab — vertical drop into the beam depth.
        // Kept separate so the no-host messages and future per-beam tuning have a home.
        return BuildSlabHosted(geom, cfg, tag, hostBb);
    }

    // ── Column below: lap inside the lower column, offset 1·d_bar along face ──

    private Result BuildColumnHosted(ColumnGeometry geom, ColumnReinforcementConfig cfg, string tag, BoundingBoxXYZ hostBb)
    {
        DowelsConfig d = cfg.Dowels;

        // The dowel laps with the CURRENT (upper) column's cage, so its bar type
        // and positions are driven by cfg.Longitudinal — even though the user
        // is allowed to override DowelBarType for special cases.
        string barTypeName = string.IsNullOrWhiteSpace(d.BarType) ? cfg.Longitudinal.BarType : d.BarType;
        if (barTypeName == "#8" && cfg.Longitudinal.BarType != "#8")
        {
            // User left DowelBarType at its default ("#8") while the longitudinal
            // is something else — almost certainly an oversight. Match the cage.
            barTypeName = cfg.Longitudinal.BarType;
        }
        RebarBarType barType = RebarFactory.GetBarType(_doc, barTypeName);
        RebarHookType? hookTop    = RebarFactory.GetHookType(_doc, d.HookTopType);
        RebarHookType? hookBottom = RebarFactory.GetHookType(_doc, d.HookBottomType);
        RebarShape?    dowelShape = RebarFactory.GetRebarShape(_doc, d.Shape);

        double extension = cfg.Ft(d.Extension);
        double lapInLower = cfg.Ft(d.Embedment);   // reused as "lap inside lower column"

        if (extension <= 0)  throw new InvalidOperationException("Dowel extension into the upper column must be positive.");
        if (lapInLower <= 0) throw new InvalidOperationException("Dowel embedment (= lap inside the lower column) must be positive.");
        if (d.Form == DowelForm.L)
            throw new InvalidOperationException(
                "L-form dowels are not supported with DowelHost=Column; the bottom is INSIDE the lower column's cage, " +
                "no slab to bend into. Use DowelForm=Straight.");

        double hostTopZ = hostBb.Max.Z;
        double hostHeight = hostBb.Max.Z - hostBb.Min.Z;
        if (lapInLower > hostHeight)
            throw new InvalidOperationException(
                $"Dowel lap inside lower column ({UnitConv.FtToIn(lapInLower):0.##}\") exceeds the lower column's height " +
                $"({UnitConv.FtToIn(hostHeight):0.##}\").");

        var positions = LongitudinalBarBuilder.ComputeCagePositions(_doc, cfg, geom);
        bool[] mask = LongitudinalBarBuilder.ResolvePositionMask(positions, d.Positions);
        double offset = barType.BarModelDiameter;   // 1·d_bar along face

        double zLocalHostTop      = hostTopZ - geom.BaseCenter.Z;
        double zLocalDowelBottom  = zLocalHostTop - lapInLower;
        double zLocalDowelTop     = zLocalHostTop + extension;

        int created = 0;
        for (int i = 0; i < positions.Count; i++)
        {
            if (!mask[i]) continue;
            (double x, double y) = positions[i];
            (double xd, double yd) = OffsetAlongFace(geom, x, y, offset);
            XYZ p0 = geom.At(xd, yd, zLocalDowelBottom);
            XYZ p1 = geom.At(xd, yd, zLocalDowelTop);

            RebarFactory.Create(
                _doc, RebarStyle.Standard, barType, geom.Instance, geom.LocalX,
                new List<Curve> { Line.CreateBound(p0, p1) }, tag,
                startHook: hookBottom, endHook: hookTop, shape: dowelShape);
            created++;
        }

        return new Result(created, null);
    }

    /// <summary>
    /// Shift a cage position by <paramref name="offset"/> tangentially along the face
    /// the bar sits on. For a bar on a left/right face (|x| ≥ |y|), the face tangent
    /// is the LocalY axis; for a bar on a top/bottom face, it's LocalX. The shift is
    /// always in the positive tangent direction, so all dowels around the cage rotate
    /// consistently (CCW when viewed from above), leaving each cage position clear
    /// for the actual cage bar that gets placed alongside.
    /// </summary>
    private static (double xd, double yd) OffsetAlongFace(ColumnGeometry geom, double x, double y, double offset)
    {
        if (geom.Section == ColumnSection.Round)
        {
            // Tangent direction is perpendicular to the radial — rotate (x, y) by +90°.
            double r = Math.Sqrt(x * x + y * y);
            if (r < 1e-9) return (x, y);
            double tx = -y / r;
            double ty =  x / r;
            return (x + tx * offset, y + ty * offset);
        }
        // Rectangular: tangent axis depends on which face the bar is on.
        if (Math.Abs(x) >= Math.Abs(y))
            return (x, y + offset);   // bar on ±X face → shift in Y
        else
            return (x + offset, y);   // bar on ±Y face → shift in X
    }

    // ── Curve helpers for slab-hosted forms ─────────────────────────────

    private static IList<Curve> Straight(
        ColumnGeometry geom, double x, double y, double zBottom, double zTop)
    {
        XYZ p0 = geom.At(x, y, zBottom);
        XYZ p1 = geom.At(x, y, zTop);
        return new List<Curve> { Line.CreateBound(p0, p1) };
    }

    private static IList<Curve> LShape(
        ColumnGeometry geom, double x, double y, double zBottom, double zTop, double legLength)
    {
        XYZ legDir  = geom.InwardDirection(x, y);
        XYZ pCorner = geom.At(x, y, zBottom);
        XYZ pLegEnd = pCorner + legDir * legLength;
        XYZ pTop    = geom.At(x, y, zTop);

        return new List<Curve>
        {
            Line.CreateBound(pLegEnd, pCorner),
            Line.CreateBound(pCorner, pTop),
        };
    }
}
