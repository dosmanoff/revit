using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;
using WallReinforcement.Geometry;

namespace WallReinforcement.Engine;

/// <summary>
/// Places the main field reinforcement as discrete Rebar SETS (not <c>AreaReinforcement</c>): on
/// each face a set of vertical bars distributed along the wall length and a set of horizontal bars
/// distributed up the height. Layer offsets come from <see cref="WallLayering"/> so the horizontal
/// bars sit outboard at the cover line and the verticals nest one bar-diameter inboard (req 5).
///
/// Field bars dodge obstructions: verticals stop above/below an opening and skip the L-corner column
/// zone (the corner's four bars are placed by <see cref="CornerColumnBuilder"/>); horizontals stop
/// left/right of an opening. The cut ends are wrapped by <see cref="OpeningEdgeBarBuilder"/>.
///
/// Edge projections (dowels): when enabled per edge, the FULL-height/length field bars extend past
/// the wall edge by a straight length and optionally finish in a 90° bend (e.g. lapping into a slab).
/// Top/bottom edges project the verticals; the wall ends project the horizontals, except where that
/// end meets another wall (the end U-bar continuity handles it instead).
/// </summary>
public class FaceBarBuilder
{
    private readonly Document _doc;

    /// <summary>Clipped field bars shorter than this (feet) are dropped — a sliver at a slope tip is
    /// useless and Revit rejects a too-short bar.</summary>
    private const double MinClippedBarFt = 0.5;

    public FaceBarBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, WallLayering lay,
                     IReadOnlyList<WallJunction> junctions, IReadOnlyList<OpeningRect> openings,
                     ISet<long> mergeOpeningIds, ElevationProfile? profile, string tag)
    {
        int n = 0;
        if (cfg.FaceMesh.Exterior is { } ext)  n += BuildFace(axes, cfg, lay, junctions, openings, mergeOpeningIds, profile, ext,  exterior: true,  tag);
        if (cfg.FaceMesh.Interior is { } intr) n += BuildFace(axes, cfg, lay, junctions, openings, mergeOpeningIds, profile, intr, exterior: false, tag);
        return n;
    }

    private int BuildFace(WallAxes axes, ReinforcementConfig cfg, WallLayering lay,
                          IReadOnlyList<WallJunction> junctions, IReadOnlyList<OpeningRect> openings,
                          ISet<long> mergeOpeningIds, ElevationProfile? profile, FaceConfig face, bool exterior, string tag)
    {
        // A non-rectangular outline (slanted end/top) needs per-bar clipping to the profile; a
        // plumb rectangular wall keeps the efficient uniform-set path.
        bool clipped = profile is not null && !profile.IsAxisAlignedRect();
        int n = 0;
        if (clipped)
        {
            n += BuildVerticalsClipped(axes, cfg, lay, openings, mergeOpeningIds, profile!, face, exterior, tag);
            n += BuildHorizontalsClipped(axes, cfg, lay, openings, profile!, face, exterior, tag);
        }
        else
        {
            n += BuildVerticals(axes, cfg, lay, junctions, openings, mergeOpeningIds, face, exterior, tag);
            n += BuildHorizontals(axes, cfg, lay, junctions, openings, face, exterior, tag);
        }
        return n;
    }

    // ── Profile-clipped field bars (non-rectangular walls): one bar per position, trimmed to the
    //    real outline (inset by cover) and split around openings. No edge projections in this path.

    private int BuildVerticalsClipped(WallAxes axes, ReinforcementConfig cfg, WallLayering lay,
                                      IReadOnlyList<OpeningRect> openings, ISet<long> mergeOpeningIds,
                                      ElevationProfile profile, FaceConfig face, bool exterior, string tag)
    {
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, face.Vertical.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;
        double endsCover = cfg.Ft(cfg.Cover.Ends), topCover = cfg.Ft(cfg.Cover.Top), bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double offV = lay.FieldFaceV(exterior), margin = endsCover;
        int n = 0;
        foreach (double u in RebarFactory.EvenlySpaced(endsCover, axes.Length - endsCover, cfg.Ft(face.Vertical.Spacing)))
        foreach (Interval span in profile.VerticalSpansAt(u))
        {
            double vb = span.From + bottomCover, vt = span.To - topCover;
            if (vt - vb <= 1e-3) continue;
            // Over a merge opening nothing goes above it (the closed stirrup covers the strip), so
            // block right up to the top; otherwise just block the opening band.
            var blocks = openings.Where(o => u >= o.UMin && u <= o.UMax).Select(o =>
                mergeOpeningIds.Contains(o.InsertId.Value)
                    ? new Interval(o.VMin - margin, vt + 1)
                    : new Interval(o.VMin - margin, o.VMax + margin));
            foreach (Interval clear in IntervalMath.Subtract(vb, vt, blocks))
            {
                if (clear.Length < MinClippedBarFt) continue;   // skip slivers at a slope tip
                RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, axes.Wall, axes.LengthDir,
                    new List<Curve> { Line.CreateBound(axes.At(u, clear.From, offV), axes.At(u, clear.To, offV)) }, tag);
                n++;
            }
        }
        return n;
    }

    private int BuildHorizontalsClipped(WallAxes axes, ReinforcementConfig cfg, WallLayering lay,
                                        IReadOnlyList<OpeningRect> openings, ElevationProfile profile,
                                        FaceConfig face, bool exterior, string tag)
    {
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, face.Horizontal.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;
        double endsCover = cfg.Ft(cfg.Cover.Ends), topCover = cfg.Ft(cfg.Cover.Top), bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double offH = lay.FieldFaceH(exterior), margin = endsCover;
        int n = 0;
        foreach (double v in RebarFactory.EvenlySpaced(bottomCover, axes.Height - topCover, cfg.Ft(face.Horizontal.Spacing)))
        foreach (Interval span in profile.HorizontalSpansAt(v))
        {
            double ua = span.From + endsCover, ub = span.To - endsCover;
            if (ub - ua <= 1e-3) continue;
            var blocks = openings.Where(o => v >= o.VMin && v <= o.VMax).Select(o => new Interval(o.UMin - margin, o.UMax + margin));
            foreach (Interval clear in IntervalMath.Subtract(ua, ub, blocks))
            {
                if (clear.Length < MinClippedBarFt) continue;   // skip slivers at a slope tip
                RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, axes.Wall, axes.HeightDir,
                    new List<Curve> { Line.CreateBound(axes.At(clear.From, v, offH), axes.At(clear.To, v, offH)) }, tag);
                n++;
            }
        }
        return n;
    }

    // ── Verticals (distributed along length); skip corner zones, split around openings ────────

    private int BuildVerticals(WallAxes axes, ReinforcementConfig cfg, WallLayering lay,
                               IReadOnlyList<WallJunction> junctions, IReadOnlyList<OpeningRect> openings,
                               ISet<long> mergeOpeningIds, FaceConfig face, bool exterior, string tag)
    {
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, face.Vertical.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double endsCover   = cfg.Ft(cfg.Cover.Ends);
        double topCover    = cfg.Ft(cfg.Cover.Top);
        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double offV        = lay.FieldFaceV(exterior);
        double vSpacing    = cfg.Ft(face.Vertical.Spacing);

        var top = EdgeProjection.From(cfg, cfg.Projections.Top,    face.Vertical.BarType);
        var bot = EdgeProjection.From(cfg, cfg.Projections.Bottom, face.Vertical.BarType);
        double vTopFull = top.On ? axes.Height + top.Length : axes.Height - topCover;
        double vBotFull = bot.On ? -bot.Length             : bottomCover;

        // Remove verticals in each L-corner's column zone (CornerColumnBuilder places the 4 bars).
        var cornerZones = new List<Interval>();
        foreach (WallJunction j in junctions.Where(x => x.Kind == JunctionKind.LCorner))
        {
            double otherHalf = WallAxes.For(j.OtherWall).HalfThickness;
            cornerZones.Add(j.OurU < axes.Length * 0.5
                ? new Interval(0, otherHalf)
                : new Interval(axes.Length - otherHalf, axes.Length));
        }

        double margin = endsCover;
        var openingU = openings.Select(o => new Interval(o.UMin, o.UMax)).ToList();
        int n = 0;
        foreach (Interval run in IntervalMath.Subtract(endsCover, axes.Length - endsCover, cornerZones))
        {
            // Full-height verticals where no opening interrupts.
            foreach (Interval seg in IntervalMath.Subtract(run.From, run.To, openingU))
                n += PlaceVerticals(axes, barTypeId, offV, seg, vSpacing, vBotFull, vTopFull, bot, top, tag);

            // Split verticals above & below each opening — the piece above still carries the top
            // projection (выпуск), the piece below the bottom projection.
            foreach (OpeningRect o in openings)
            {
                double a = Math.Max(run.From, o.UMin), b = Math.Min(run.To, o.UMax);
                if (b - a <= vSpacing) continue;
                n += PlaceVerticals(axes, barTypeId, offV, new Interval(a, b), vSpacing,
                                    vBotFull, o.VMin - margin, bot, EdgeProjection.Off, tag);
                // Above a merge opening the closed stirrup replaces this top piece.
                if (!mergeOpeningIds.Contains(o.InsertId.Value))
                    n += PlaceVerticals(axes, barTypeId, offV, new Interval(a, b), vSpacing,
                                        o.VMax + margin, vTopFull, EdgeProjection.Off, top, tag);
            }
        }
        return n;
    }

    private int PlaceVerticals(WallAxes axes, ElementId barTypeId, double offV, Interval u, double vSpacing,
                               double vBot, double vTop, EdgeProjection bot, EdgeProjection top, string tag)
    {
        if (vTop - vBot <= 1e-3) return 0;
        var (count, spacing, first) = RebarFactory.UniformLayout(u.From, u.To, vSpacing);
        if (count == 0) return 0;

        bool alt = top.Bend == BendDir.Alternate || bot.Bend == BendDir.Alternate;
        if (!alt)
            return EmitVertical(axes, barTypeId, offV, first, count, spacing, vBot, vTop, bot, top, +1, tag);

        int countEven = (count + 1) / 2, countOdd = count / 2;
        int n = EmitVertical(axes, barTypeId, offV, first, countEven, spacing * 2, vBot, vTop, bot, top, +1, tag);
        if (countOdd > 0)
            n += EmitVertical(axes, barTypeId, offV, first + spacing, countOdd, spacing * 2, vBot, vTop, bot, top, -1, tag);
        return n;
    }

    private int EmitVertical(WallAxes axes, ElementId barTypeId, double offV, double uFirst,
                             int count, double spacing, double vBot, double vTop,
                             EdgeProjection bot, EdgeProjection top, int altSign, string tag)
    {
        var pts = new List<XYZ>();
        if (bot.BendSign(altSign) is { } bs) pts.Add(axes.At(uFirst, vBot, offV + bs * bot.BendLength));
        pts.Add(axes.At(uFirst, vBot, offV));
        pts.Add(axes.At(uFirst, vTop, offV));
        if (top.BendSign(altSign) is { } ts) pts.Add(axes.At(uFirst, vTop, offV + ts * top.BendLength));

        RebarFactory.CreateSet(_doc, RebarStyle.Standard, barTypeId, axes.Wall, axes.LengthDir,
                               Polyline(pts), count, spacing, tag);
        return count;
    }

    // ── Horizontals (distributed up height); split around openings; ends may project ──────────

    private int BuildHorizontals(WallAxes axes, ReinforcementConfig cfg, WallLayering lay,
                                 IReadOnlyList<WallJunction> junctions, IReadOnlyList<OpeningRect> openings,
                                 FaceConfig face, bool exterior, string tag)
    {
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, face.Horizontal.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double endsCover   = cfg.Ft(cfg.Cover.Ends);
        double topCover    = cfg.Ft(cfg.Cover.Top);
        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double offH        = lay.FieldFaceH(exterior);
        double hSpacing    = cfg.Ft(face.Horizontal.Spacing);

        bool startJoined = junctions.Any(j => j.OurU < axes.Length * 0.5);
        bool endJoined   = junctions.Any(j => j.OurU >= axes.Length * 0.5);
        var ends  = EdgeProjection.From(cfg, cfg.Projections.Ends, face.Horizontal.BarType);
        var start = startJoined ? EdgeProjection.Off : ends;
        var fin   = endJoined   ? EdgeProjection.Off : ends;
        double uStartFull = start.On ? -start.Length            : endsCover;
        double uEndFull   = fin.On   ? axes.Length + fin.Length : axes.Length - endsCover;

        double margin = endsCover;
        var openingV = openings.Select(o => new Interval(o.VMin, o.VMax)).ToList();
        int n = 0;
        // Full-length horizontals where no opening interrupts.
        foreach (Interval seg in IntervalMath.Subtract(bottomCover, axes.Height - topCover, openingV))
            n += PlaceHorizontals(axes, barTypeId, offH, seg, hSpacing, uStartFull, uEndFull, start, fin, tag);

        // Split horizontals left & right of each opening.
        foreach (OpeningRect o in openings)
        {
            double a = Math.Max(bottomCover, o.VMin), b = Math.Min(axes.Height - topCover, o.VMax);
            if (b - a <= hSpacing) continue;
            n += PlaceHorizontals(axes, barTypeId, offH, new Interval(a, b), hSpacing,
                                  endsCover, o.UMin - margin, EdgeProjection.Off, EdgeProjection.Off, tag);
            n += PlaceHorizontals(axes, barTypeId, offH, new Interval(a, b), hSpacing,
                                  o.UMax + margin, axes.Length - endsCover, EdgeProjection.Off, EdgeProjection.Off, tag);
        }
        return n;
    }

    private int PlaceHorizontals(WallAxes axes, ElementId barTypeId, double offH, Interval v, double hSpacing,
                                 double uStart, double uEnd, EdgeProjection start, EdgeProjection fin, string tag)
    {
        if (uEnd - uStart <= 1e-3) return 0;
        var (count, spacing, first) = RebarFactory.UniformLayout(v.From, v.To, hSpacing);
        if (count == 0) return 0;

        bool alt = start.Bend == BendDir.Alternate || fin.Bend == BendDir.Alternate;
        if (!alt)
            return EmitHorizontal(axes, barTypeId, offH, first, count, spacing, uStart, uEnd, start, fin, +1, tag);

        int countEven = (count + 1) / 2, countOdd = count / 2;
        int n = EmitHorizontal(axes, barTypeId, offH, first, countEven, spacing * 2, uStart, uEnd, start, fin, +1, tag);
        if (countOdd > 0)
            n += EmitHorizontal(axes, barTypeId, offH, first + spacing, countOdd, spacing * 2, uStart, uEnd, start, fin, -1, tag);
        return n;
    }

    private int EmitHorizontal(WallAxes axes, ElementId barTypeId, double offH, double vFirst,
                               int count, double spacing, double uStart, double uEnd,
                               EdgeProjection start, EdgeProjection fin, int altSign, string tag)
    {
        var pts = new List<XYZ>();
        if (start.BendSign(altSign) is { } ss) pts.Add(axes.At(uStart, vFirst, offH + ss * start.BendLength));
        pts.Add(axes.At(uStart, vFirst, offH));
        pts.Add(axes.At(uEnd,   vFirst, offH));
        if (fin.BendSign(altSign) is { } fs) pts.Add(axes.At(uEnd, vFirst, offH + fs * fin.BendLength));

        RebarFactory.CreateSet(_doc, RebarStyle.Standard, barTypeId, axes.Wall, axes.HeightDir,
                               Polyline(pts), count, spacing, tag);
        return count;
    }

    private static IList<Curve> Polyline(IReadOnlyList<XYZ> pts)
    {
        var curves = new List<Curve>();
        for (int i = 0; i < pts.Count - 1; i++)
            if (pts[i].DistanceTo(pts[i + 1]) > 1e-7)
                curves.Add(Line.CreateBound(pts[i], pts[i + 1]));
        return curves;
    }
}

/// <summary>Resolved per-edge projection geometry for the field bars (feet).</summary>
internal readonly struct EdgeProjection
{
    public bool On { get; private init; }
    public double Length { get; private init; }
    public bool HasBend { get; private init; }
    public double BendLength { get; private init; }
    public BendDir Bend { get; private init; }

    public static readonly EdgeProjection Off = new() { On = false };

    public static EdgeProjection From(ReinforcementConfig cfg, EdgeProjectionConfig p, string barType)
    {
        if (!p.Enabled) return Off;
        // A projection laps the bar in the next pour, so length 0 ⇒ the Class B tension lap. A 90°
        // bend anchors into a slab, so bendLength 0 ⇒ the development length ℓd.
        double len = Resolve(cfg, p.Length, barType, lap: true);
        if (len <= 1e-6) return Off;
        return new EdgeProjection
        {
            On = true,
            Length = len,
            HasBend = p.Bend90,
            BendLength = p.Bend90 ? Resolve(cfg, p.BendLength, barType, lap: false) : 0,
            Bend = p.BendDir,
        };
    }

    /// <summary>Signed bend direction across the thickness (−1 interior, +1 exterior), or null when
    /// there is no 90° leg. <paramref name="altSign"/> picks the side for <see cref="BendDir.Alternate"/>.</summary>
    public double? BendSign(int altSign) => !On || !HasBend || BendLength <= 1e-6
        ? null
        : Bend switch
        {
            BendDir.Interior  => -1.0,
            BendDir.Exterior  => +1.0,
            BendDir.Alternate => altSign,
            _ => null,
        };

    private static double Resolve(ReinforcementConfig cfg, WallReinforcement.Config.Length l, string barType, bool lap)
    {
        double explicitFt = cfg.Ft(l);
        if (explicitFt > 1e-9) return explicitFt;
        return lap ? cfg.LapFeet(barType, 0) : cfg.DevLengthFeet(barType, 0);
    }
}
