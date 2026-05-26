using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Places foundation dowels (starter bars) that lap with the column longitudinal
/// cage and embed into the slab below. One dowel per longitudinal-bar position.
///
/// <para>Supported forms:
/// <list type="bullet">
///   <item><see cref="DowelForm.Straight"/> — single vertical bar.</item>
///   <item><see cref="DowelForm.L"/> — 90° bend at the bottom; horizontal leg
///         extends inside the slab toward the column centre.</item>
/// </list>
/// </para>
///
/// <para>When the host context has no slab below, the builder silently places
/// no dowels — the column is treated as having no foundation anchorage to
/// generate. This is reported as "no slab below" in the run summary so the
/// user can decide whether that was intentional.</para>
/// </summary>
public class FoundationDowelBuilder
{
    private readonly Document _doc;

    public FoundationDowelBuilder(Document doc) => _doc = doc;

    /// <summary>
    /// Output of one column's dowel-placement step. <see cref="Created"/> is the
    /// number of dowel rebars placed; <see cref="SkipReason"/> is non-null only
    /// when the step was a no-op for a reportable reason (no slab found, etc).
    /// </summary>
    public record struct Result(int Created, string? SkipReason);

    public Result Build(ColumnGeometry geom, ColumnReinforcementConfig cfg, string tag, Element? slabBelow)
    {
        DowelsConfig d = cfg.Dowels;
        if (!d.Enabled) return new Result(0, null);
        if (slabBelow is null)
        {
            string searched = d.OnlyStructuralFoundation
                ? "Structural Foundation"
                : "Structural Foundation or Floor";
            return new Result(0,
                $"Dowels enabled but no {searched} found directly below the column. " +
                (d.OnlyStructuralFoundation
                    ? "Either re-categorise the foundation, or set dowels.onlyStructuralFoundation=false to also search Floors."
                    : "Check that the slab's plan extent includes the column centreline."));
        }

        BoundingBoxXYZ slabBb = slabBelow.get_BoundingBox(null)
            ?? throw new InvalidOperationException(
                $"Slab below column {geom.Instance.Id.Value} has no bounding box.");

        double slabTopWorldZ = slabBb.Max.Z;
        double slabBottomWorldZ = slabBb.Min.Z;

        RebarBarType barType = RebarFactory.GetBarType(_doc, d.BarType);
        RebarHookType? hookTop    = RebarFactory.GetHookType(_doc, d.HookTopType);
        RebarHookType? hookBottom = RebarFactory.GetHookType(_doc, d.HookBottomType);

        double extension = cfg.Ft(d.Extension);
        double embedment = cfg.Ft(d.Embedment);
        double legLength = cfg.Ft(d.LegLength);

        if (extension <= 0)
            throw new InvalidOperationException("Dowel extension above slab must be positive.");
        if (embedment <= 0)
            throw new InvalidOperationException("Dowel embedment into slab must be positive.");
        if (d.Form == DowelForm.L && legLength <= 0)
            throw new InvalidOperationException("L-form dowel leg length must be positive.");

        double slabThickness = slabTopWorldZ - slabBottomWorldZ;
        if (embedment > slabThickness)
            throw new InvalidOperationException(
                $"Dowel embedment ({UnitConv.FtToIn(embedment):0.##}\") exceeds slab thickness " +
                $"({UnitConv.FtToIn(slabThickness):0.##}\").");

        var positions = LongitudinalBarBuilder.ComputeCagePositions(_doc, cfg, geom);

        // Local z (relative to column base) of the slab top and dowel bottom.
        double zLocalSlabTop      = slabTopWorldZ - geom.BaseCenter.Z;
        double zLocalDowelBottom  = zLocalSlabTop - embedment;
        double zLocalDowelTop     = zLocalSlabTop + extension;

        int created = 0;
        foreach ((double x, double y) in positions)
        {
            IList<Curve> curves = d.Form switch
            {
                DowelForm.Straight => BuildStraight(geom, x, y, zLocalDowelBottom, zLocalDowelTop),
                DowelForm.L        => BuildL(geom, x, y, zLocalDowelBottom, zLocalDowelTop, legLength),
                _ => throw new InvalidOperationException($"Unknown dowel form: {d.Form}"),
            };

            // Normal: perpendicular to the bar's bending plane.
            //   Straight bar: any horizontal direction works.
            //   L-form: perpendicular to the leg + Z plane = NormalForBendAt.
            XYZ normal = d.Form == DowelForm.Straight ? geom.LocalX : geom.NormalForBendAt(x, y);

            RebarFactory.Create(
                _doc,
                RebarStyle.Standard,
                barType,
                geom.Instance,
                normal,
                curves,
                tag,
                startHook: hookBottom,
                endHook:   hookTop);
            created++;
        }

        return new Result(created, null);
    }

    private static IList<Curve> BuildStraight(
        ColumnGeometry geom, double x, double y, double zBottom, double zTop)
    {
        XYZ p0 = geom.At(x, y, zBottom);
        XYZ p1 = geom.At(x, y, zTop);
        return new List<Curve> { Line.CreateBound(p0, p1) };
    }

    private static IList<Curve> BuildL(
        ColumnGeometry geom, double x, double y, double zBottom, double zTop, double legLength)
    {
        // Horizontal leg extends inward into the slab — perpendicular to the
        // nearest face for rectangular columns; radially toward the centre for
        // round columns. Both rules live in ColumnGeometry.InwardDirection.
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
