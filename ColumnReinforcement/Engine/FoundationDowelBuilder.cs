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
        if (slabBelow is null) return new Result(0, "Dowels enabled but no slab found below the column.");

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

        var bounds = LongitudinalBarBuilder.ComputeCageBounds(_doc, cfg, geom);
        var positions = LongitudinalBarBuilder.LayoutPositions(
            cfg.Longitudinal, bounds.xMin, bounds.xMax, bounds.yMin, bounds.yMax);

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

            // Normal: perpendicular to the bar's bending plane. For an L-bend in the
            // (LocalX, LocalZ) plane the normal is LocalY, and vice versa. For a
            // straight bar any horizontal direction works — use the leg's perpendicular.
            XYZ normal = ChooseNormal(geom, x, y, d.Form);

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
        // The L's vertical leg sits at (x, y). The horizontal leg extends inward
        // (toward the column centre) from the corner of the L. For corner bars
        // (|x| ≈ x_max, |y| ≈ y_max) the leg direction is whichever axis has the
        // larger magnitude — i.e. the face we are nearest to. Ties between the two
        // are broken in favour of LocalX so the choice is deterministic.
        XYZ legDir = Math.Abs(x) >= Math.Abs(y)
            ? geom.LocalX * -Math.Sign(x == 0 ? 1 : x)
            : geom.LocalY * -Math.Sign(y == 0 ? 1 : y);

        XYZ pCorner = geom.At(x, y, zBottom);                 // bottom of vertical leg = corner of L
        XYZ pLegEnd = pCorner + legDir * legLength;           // inside the slab, away from the column face
        XYZ pTop    = geom.At(x, y, zTop);                    // top of vertical leg, above the slab

        return new List<Curve>
        {
            Line.CreateBound(pLegEnd, pCorner),
            Line.CreateBound(pCorner, pTop),
        };
    }

    private static XYZ ChooseNormal(ColumnGeometry geom, double x, double y, DowelForm form)
    {
        if (form == DowelForm.Straight)
            return geom.LocalX;

        // L's two legs lie in a vertical plane spanned by the leg direction and world Z.
        // The normal of that plane is the in-plan axis perpendicular to the leg.
        return Math.Abs(x) >= Math.Abs(y) ? geom.LocalY : geom.LocalX;
    }
}
