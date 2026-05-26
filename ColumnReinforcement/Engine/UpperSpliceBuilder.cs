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
            return new Result(0,
                "Bent upper splice requires a Floor above the column; none found. " +
                "Either model the slab above as an OST_Floors element, switch the splice form to Straight " +
                "(with ignoreSlabAbove=true), or use Cranked if the upper element is a smaller column.");

        RebarBarType barType = RebarFactory.GetBarType(_doc, u.BarType);
        RebarHookType? hookTop    = RebarFactory.GetHookType(_doc, u.HookTopType);
        RebarHookType? hookBottom = RebarFactory.GetHookType(_doc, u.HookBottomType);

        double lap         = cfg.Ft(u.LapInsideColumn);
        double extension   = cfg.Ft(u.Extension);
        double bentLeg     = cfg.Ft(u.BentLegLength);
        double upperInset  = cfg.Ft(u.UpperCageInset);
        double lowerBendOffset = cfg.Ft(u.LowerBendOffsetFromTop);
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
        if (u.Form == UpperSpliceForm.Cranked)
        {
            if (upperInset <= 0)
                throw new InvalidOperationException("Cranked upper splice upperCageInset must be positive.");
            if (u.CrankedSlopeRatio <= 0)
                throw new InvalidOperationException("Cranked upper splice crankedSlopeRatio must be positive.");
            if (lowerBendOffset <= 0)
                throw new InvalidOperationException("Cranked upper splice lowerBendOffsetFromTop must be positive.");
            if (extension <= 0)
                throw new InvalidOperationException("Cranked upper splice extension (upper-column lap) must be positive.");
        }

        var positions = LongitudinalBarBuilder.ComputeCagePositions(_doc, cfg, geom);

        // Splice starts inside the column for the lap.
        double zLocalSpliceBottom = geom.Height - lap;

        // Compute the top end (and bend points where applicable) once; per-position curves use these.
        double zLocalSpliceTop = 0;
        double zLocalBendPoint = 0;
        double zLocalCrankBendLow = 0;
        double zLocalCrankBendHigh = 0;
        double zLocalCrankTop = 0;

        switch (u.Form)
        {
            case UpperSpliceForm.Straight:
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
                break;

            case UpperSpliceForm.Bent:
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
                break;

            case UpperSpliceForm.Cranked:
                {
                    // Diagonal rise = (vertical / horizontal) · horizontal offset.
                    // For corner bars the offset has two components; the magnitude is
                    // sqrt(inset² + inset²) = inset · √2. Single-direction offsets
                    // (mid-face bars) get rise = ratio · inset directly. To keep the
                    // slope ≤ 1:ratio in every direction the bar might offset, size the
                    // rise off the worst case (corner) up front.
                    double offsetMagnitudeCorner = upperInset * Math.Sqrt(2.0);
                    double diagonalRise = u.CrankedSlopeRatio * offsetMagnitudeCorner;

                    zLocalCrankBendLow  = geom.Height - lowerBendOffset;
                    zLocalCrankBendHigh = zLocalCrankBendLow + diagonalRise;
                    zLocalCrankTop      = zLocalCrankBendHigh + extension;

                    if (zLocalCrankBendLow <= zLocalSpliceBottom)
                        throw new InvalidOperationException(
                            $"Cranked upper splice lower bend ({UnitConv.FtToIn(zLocalCrankBendLow):0.##}\") is at or " +
                            $"below the lap start ({UnitConv.FtToIn(zLocalSpliceBottom):0.##}\"). " +
                            $"Reduce lap or lowerBendOffsetFromTop, or check the column height.");
                }
                break;
        }

        int created = 0;
        foreach ((double x, double y) in positions)
        {
            (IList<Curve> curves, XYZ normal) = u.Form switch
            {
                UpperSpliceForm.Straight => (BuildStraight(geom, x, y, zLocalSpliceBottom, zLocalSpliceTop), geom.LocalX),
                UpperSpliceForm.Bent     => (BuildBent(geom, x, y, zLocalSpliceBottom, zLocalBendPoint, bentLeg), geom.NormalForBendAt(x, y)),
                UpperSpliceForm.Cranked  => BuildCranked(geom, x, y, zLocalSpliceBottom, zLocalCrankBendLow, zLocalCrankBendHigh, zLocalCrankTop, upperInset),
                _ => throw new InvalidOperationException($"Unknown upper-splice form: {u.Form}"),
            };

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

    /// <summary>
    /// Cranked Z-shape: vertical inside the lower column → diagonal offsetting
    /// to the upper column's cage position → vertical inside the upper column.
    /// Degenerates to a single straight segment for bars where the upper
    /// position equals the lower position (e.g. a bar on the column axis with
    /// no in-direction offset).
    /// </summary>
    private static (IList<Curve> curves, XYZ normal) BuildCranked(
        ColumnGeometry geom,
        double x, double y,
        double zBottom, double zBendLow, double zBendHigh, double zTop,
        double upperInset)
    {
        double xu = x - Math.Sign(x) * upperInset;
        double yu = y - Math.Sign(y) * upperInset;
        double offsetMagnitude = Math.Sqrt(Math.Pow(xu - x, 2) + Math.Pow(yu - y, 2));

        if (offsetMagnitude < 1e-9)
        {
            // Bar sits on the column axis in at least one coordinate and the cranked
            // offset on that axis is therefore zero — no bend needed. Place a single
            // straight bar from the lap start to the top of the upper-column leg.
            return (
                new List<Curve> { Line.CreateBound(geom.At(x, y, zBottom), geom.At(x, y, zTop)) },
                geom.LocalX);
        }

        XYZ p1 = geom.At(x,  y,  zBottom);
        XYZ p2 = geom.At(x,  y,  zBendLow);
        XYZ p3 = geom.At(xu, yu, zBendHigh);
        XYZ p4 = geom.At(xu, yu, zTop);

        // Normal of the (vertical-diagonal-vertical) bending plane: perpendicular
        // to the horizontal offset direction. All three segments live in the plane
        // spanned by that horizontal direction and world Z, so this is exact.
        XYZ offsetDir = new XYZ(xu - x, yu - y, 0).Normalize();
        XYZ normal    = XYZ.BasisZ.CrossProduct(offsetDir).Normalize();

        return (
            new List<Curve>
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
            },
            normal);
    }
}
