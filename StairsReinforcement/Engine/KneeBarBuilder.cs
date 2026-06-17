using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Engine;

/// <summary>
/// Reinforces each flight↔landing fold (knee) across the width. The soffit (convex) corner always
/// gets a continuous bent bar carrying bottom continuity around the fold. The top (re-entrant,
/// concave) corner is treated per <see cref="KneeMode"/> — the cardinal rule is that top tension
/// steel must NOT bend around a re-entrant corner:
/// <list type="bullet">
/// <item><c>LappedHairpin</c> (default): a flight-top bar and a landing-top bar that lap past the
/// corner, each anchored in its own component — never bent through the re-entrant.</item>
/// <item><c>CrossedAtReentrant</c>: a diagonal bar crossing the corner.</item>
/// <item><c>ContinuousBent</c>: one bar bent around the corner (offered, but only sound on the soffit).</item>
/// </list>
/// </summary>
public sealed class KneeBarBuilder
{
    private readonly Document _doc;
    public KneeBarBuilder(Document doc) => _doc = doc;

    public int Build(StairAssembly asm, StairsReinforcementConfig cfg, ElementId stairId)
    {
        KneeConfig knee = cfg.Connections.Knee;
        if (!knee.Enabled) return 0;

        RebarBarType bt = RebarFactory.GetBarType(_doc, knee.BarType);
        double db = bt.BarNominalDiameter;
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.Knee);

        int created = 0;
        foreach (FlightComponent f in asm.Flights)
        {
            if (!f.RebarHostOk) continue;
            if (FindLanding(asm, f.LowerSupport) is { } lowLanding)
                created += BuildKnee(f, lowLanding, cfg, knee, bt, db, atUpper: false, tag);
            if (FindLanding(asm, f.UpperSupport) is { } topLanding)
                created += BuildKnee(f, topLanding, cfg, knee, bt, db, atUpper: true, tag);
        }
        return created;
    }

    private int BuildKnee(FlightComponent f, LandingComponent l, StairsReinforcementConfig cfg,
        KneeConfig knee, RebarBarType bt, double db, bool atUpper, string tag)
    {
        double nBot = cfg.Ft(cfg.Cover.Bottom) + db / 2;
        double nTop = f.WaistFt - cfg.Ft(cfg.Cover.Top) - db / 2;
        if (nTop <= nBot) nTop = f.WaistFt * 0.6;

        double uEnd = atUpper ? f.SlopeLengthFt : 0;
        double sgn = atUpper ? -1 : 1;            // toward the flight interior
        double leg = cfg.Ft(knee.Leg);
        double lap = BuildUtil.LapFt(cfg, db);

        double coverSide = cfg.Ft(cfg.Cover.Side);
        double w0 = -f.WidthFt / 2 + coverSide + db / 2;
        double span = f.WidthFt - 2 * (coverSide + db / 2);
        if (span < 0) { w0 = 0; span = 0; }
        (int count, double spacing) = BuildUtil.ResolveSet(knee.SpacingMode, knee.Count, cfg.Ft(knee.Spacing), span);

        var Wv = new XYZ(f.Frame.W.X, f.Frame.W.Y, 0);
        XYZ normalW = Wv.IsZeroLength() ? XYZ.BasisX : Wv.Normalize();

        XYZ foldBot = BuildUtil.XYZof(f.Frame.At(uEnd, w0, nBot));
        XYZ foldTop = BuildUtil.XYZof(f.Frame.At(uEnd, w0, nTop));

        // The landing leg continues in the flight's HORIZONTAL run direction, away from the flight
        // interior. That direction is ⟂ W, so the (multi-segment) knee bar stays planar — which
        // CreateFromCurves requires. Aiming at the landing centre instead tilts it out of plane and
        // throws "internal error" (the bar's curves must all lie in the plane ⟂ the normal).
        Pt2 runH = new Pt2(f.Frame.U.X, f.Frame.U.Y).Normalized();
        double awaySign = atUpper ? 1.0 : -1.0;            // away from the flight interior
        var into = new XYZ(runH.X * awaySign, runH.Y * awaySign, 0);

        double landBotZ = (l.ElevationFt - l.ThicknessFt) + cfg.Ft(cfg.Cover.Bottom) + db / 2;
        double landTopZ = l.ElevationFt - cfg.Ft(cfg.Cover.Top) - db / 2;

        int created = 0;

        // Soffit continuity bar (always): flight-bottom leg → fold → landing-bottom leg.
        XYZ flightBotLeg = BuildUtil.XYZof(f.Frame.At(uEnd + sgn * leg, w0, nBot));
        XYZ landBotLeg = new(foldBot.X + into.X * leg, foldBot.Y + into.Y * leg, landBotZ);
        created += Set(bt, f.Host, normalW, count, spacing, tag,
            Line.CreateBound(flightBotLeg, foldBot), Line.CreateBound(foldBot, landBotLeg));

        // Top treatment.
        switch (knee.Mode)
        {
            case KneeMode.ContinuousBent:
                XYZ ftLeg = BuildUtil.XYZof(f.Frame.At(uEnd + sgn * leg, w0, nTop));
                XYZ ltLeg = new(foldTop.X + into.X * leg, foldTop.Y + into.Y * leg, landTopZ);
                created += Set(bt, f.Host, normalW, count, spacing, tag,
                    Line.CreateBound(ftLeg, foldTop), Line.CreateBound(foldTop, ltLeg));
                break;

            case KneeMode.CrossedAtReentrant:
                XYZ c0 = BuildUtil.XYZof(f.Frame.At(uEnd + sgn * leg, w0, nTop));
                XYZ c1 = new(foldTop.X + into.X * leg, foldTop.Y + into.Y * leg, landTopZ);
                created += Set(bt, f.Host, normalW, count, spacing, tag, Line.CreateBound(c0, c1));
                break;

            case KneeMode.LappedHairpin:
            default:
                XYZ flightLap = BuildUtil.XYZof(f.Frame.At(uEnd + sgn * (leg + lap), w0, nTop));
                XYZ landLap = new(foldTop.X + into.X * (leg + lap), foldTop.Y + into.Y * (leg + lap), landTopZ);
                created += Set(bt, f.Host, normalW, count, spacing, tag, Line.CreateBound(flightLap, foldTop));
                created += Set(bt, f.Host, normalW, count, spacing, tag, Line.CreateBound(foldTop, landLap));
                break;
        }

        return created;
    }

    private int Set(RebarBarType bt, Element host, XYZ normal, int count, double spacing, string tag, params Curve[] curves) =>
        RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, host, normal, curves.ToList(), tag, count, spacing);

    private static LandingComponent? FindLanding(StairAssembly asm, SupportInfo? support) =>
        support?.Kind == "landing"
            ? asm.Landings.FirstOrDefault(l => l.ComponentId.Value == support.ElementId)
            : null;
}
