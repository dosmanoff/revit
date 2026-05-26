using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Places upper splices — continuation bars that lap with the column longitudinal
/// near the top of the column and extend above the column top. One splice per
/// longitudinal-bar position.
///
/// <para>Supported forms:
/// <list type="bullet">
///   <item><see cref="UpperSpliceForm.Straight"/> — vertical bar continuing past
///         the column top. At an intermediate floor, the extension is measured
///         from the slab top above; at a roof level or when
///         <see cref="UpperSplicesConfig.IgnoreSlabAbove"/> is set, from the
///         column top.</item>
///   <item><see cref="UpperSpliceForm.Bent"/> — vertical leg up to just below the
///         slab top above, then 90° bend with a horizontal leg anchored inside
///         the slab. Requires a slab above; no slab → reported skip.</item>
/// </list>
/// </para>
/// </summary>
public class UpperSpliceBuilder
{
    private readonly Document _doc;

    public UpperSpliceBuilder(Document doc) => _doc = doc;

    /// <summary>
    /// Output of one column's upper-splice step. <see cref="SkipReason"/> is
    /// non-null only when the step was a no-op for a reportable reason
    /// (bent form requested but no slab above, etc).
    /// </summary>
    public record struct Result(int Created, string? SkipReason);

    public Result Build(ColumnGeometry geom, ColumnReinforcementConfig cfg, string tag, Element? slabAbove)
    {
        UpperSplicesConfig u = cfg.UpperSplices;
        if (!u.Enabled) return new Result(0, null);

        if (u.Form == UpperSpliceForm.Bent && slabAbove is null)
            return new Result(0, "Bent upper splice requires a slab above the column; none found.");

        RebarBarType barType = RebarFactory.GetBarType(_doc, u.BarType);
        RebarHookType? hookTop    = RebarFactory.GetHookType(_doc, u.HookTopType);
        RebarHookType? hookBottom = RebarFactory.GetHookType(_doc, u.HookBottomType);

        double lap         = cfg.Ft(u.LapInsideColumn);
        double extension   = cfg.Ft(u.Extension);
        double bentLeg     = cfg.Ft(u.BentLegLength);
        double endCover    = cfg.Ft(cfg.Cover.Ends);

        if (lap <= 0)
            throw new InvalidOperationException("Upper splice lapInsideColumn must be positive.");
        if (lap > geom.Height)
            throw new InvalidOperationException(
                $"Upper splice lap ({UnitConv.FtToIn(lap):0.##}\") exceeds column height " +
                $"({UnitConv.FtToIn(geom.Height):0.##}\").");
        if (u.Form == UpperSpliceForm.Straight && extension <= 0)
            throw new InvalidOperationException("Straight upper splice extension must be positive.");
        if (u.Form == UpperSpliceForm.Bent && bentLeg <= 0)
            throw new InvalidOperationException("Bent upper splice leg length must be positive.");

        var positions = LongitudinalBarBuilder.ComputeCagePositions(_doc, cfg, geom);

        // Splice starts inside the column for the lap.
        double zLocalSpliceBottom = geom.Height - lap;

        // Compute the top end (and bend point for bent form) once; per-position curves use these.
        double zLocalSpliceTop = 0;
        double zLocalBendPoint = 0;

        if (u.Form == UpperSpliceForm.Straight)
        {
            if (slabAbove is not null && !u.IgnoreSlabAbove)
            {
                BoundingBoxXYZ slabBb = slabAbove.get_BoundingBox(null)
                    ?? throw new InvalidOperationException("Slab above has no bounding box.");
                double slabTopWorldZ = slabBb.Max.Z;
                zLocalSpliceTop = (slabTopWorldZ - geom.BaseCenter.Z) + extension;
            }
            else
            {
                zLocalSpliceTop = geom.Height + extension;
            }
        }
        else // Bent
        {
            BoundingBoxXYZ slabBb = slabAbove!.get_BoundingBox(null)
                ?? throw new InvalidOperationException("Slab above has no bounding box.");
            double slabTopWorldZ = slabBb.Max.Z;
            // Bend just below the top of the slab, inside the top cover layer.
            zLocalBendPoint = (slabTopWorldZ - endCover) - geom.BaseCenter.Z;

            if (zLocalBendPoint <= zLocalSpliceBottom)
                throw new InvalidOperationException(
                    $"Bent upper splice bend point ({UnitConv.FtToIn(zLocalBendPoint):0.##}\") is at or below " +
                    $"the lap start ({UnitConv.FtToIn(zLocalSpliceBottom):0.##}\"). " +
                    $"Increase column height, decrease lap, or check slab elevation.");
        }

        int created = 0;
        foreach ((double x, double y) in positions)
        {
            IList<Curve> curves = u.Form switch
            {
                UpperSpliceForm.Straight => BuildStraight(geom, x, y, zLocalSpliceBottom, zLocalSpliceTop),
                UpperSpliceForm.Bent     => BuildBent(geom, x, y, zLocalSpliceBottom, zLocalBendPoint, bentLeg),
                _ => throw new InvalidOperationException($"Unknown upper-splice form: {u.Form}"),
            };

            XYZ normal = u.Form == UpperSpliceForm.Straight ? geom.LocalX : geom.NormalForBendAt(x, y);

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

    private static IList<Curve> BuildBent(
        ColumnGeometry geom, double x, double y, double zBottom, double zBend, double legLength)
    {
        // Horizontal leg direction lives in ColumnGeometry: rectangular → perpendicular
        // to nearest face; round → radially toward the centre.
        XYZ legDir  = geom.InwardDirection(x, y);
        XYZ pBottom = geom.At(x, y, zBottom);
        XYZ pCorner = geom.At(x, y, zBend);
        XYZ pLegEnd = pCorner + legDir * legLength;

        return new List<Curve>
        {
            Line.CreateBound(pBottom, pCorner),
            Line.CreateBound(pCorner, pLegEnd),
        };
    }
}
