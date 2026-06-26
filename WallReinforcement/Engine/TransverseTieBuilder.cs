using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;
using WallReinforcement.Geometry;

namespace WallReinforcement.Engine;

/// <summary>
/// Places transverse ties (crossties across the wall thickness) at a grid spacing.
/// Skipped for walls thinner than <see cref="TiesConfig.MinThickness"/>.
///
/// A tie is a single straight bar in the section plane (perpendicular to the wall length) running
/// from the exterior-face cover line to the interior-face cover line, hooked at each end. Each
/// height row is laid as one or more uniform SETS that SKIP the wall openings (a tie must not pass
/// through an opening), splitting the row into clear runs around each opening.
/// </summary>
public class TransverseTieBuilder
{
    private readonly Document _doc;

    public TransverseTieBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, WallLayering lay,
                     IReadOnlyList<OpeningRect> openings, string tag)
    {
        TiesConfig ties = cfg.Ties;
        if (!ties.Enabled) return 0;
        if (axes.Thickness < cfg.Ft(ties.MinThickness)) return 0;

        ElementId barTypeId = RebarFactory.LookupBarType(_doc, ties.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        // A crosstie ("шпилька") is a StirrupTie-style bar with a 135° tie hook at each end engaging
        // both face mats. The hook type MUST match the bar style — a Standard bar + a Stirrup/Tie
        // hook throws an internal error. If the model has no tie hook, fall back to a straight
        // Standard bar (no hook).
        ElementId hookId = RebarFactory.LookupHookType(_doc, "Stirrup/Tie - 135", "Stirrup/Tie - 90", "Stirrup/Tie");
        RebarStyle tieStyle = hookId != ElementId.InvalidElementId ? RebarStyle.StirrupTie : RebarStyle.Standard;

        double endsCover   = cfg.Ft(cfg.Cover.Ends);
        double topCover    = cfg.Ft(cfg.Cover.Top);
        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double extOffset   = lay.TieFace(true);   // tie wraps at the cover, outside the H/V layers
        double intOffset   = lay.TieFace(false);
        double sx          = cfg.Ft(ties.SpacingX);
        double sy          = cfg.Ft(ties.SpacingY);
        double margin      = cfg.Ft(ties.OpeningMargin);
        if (margin <= 1e-9) margin = sx;   // default clearance = one X-spacing

        int count = 0;
        // One or more SETS per height row: each row distributed along the wall length, split into the
        // clear runs that avoid openings.
        foreach (double v in RebarFactory.EvenlySpaced(bottomCover, axes.Height - topCover, sy))
        {
            var blocked = openings
                .Where(o => v >= o.VMin - margin && v <= o.VMax + margin)
                .Select(o => new Interval(o.UMin - margin, o.UMax + margin));

            foreach (Interval run in IntervalMath.Subtract(endsCover, axes.Length - endsCover, blocked))
            {
                if (run.Length < sx) continue;   // sliver next to an opening — leave to the trim bars

                var (uCount, uSpacing, uFirst) = RebarFactory.UniformLayout(run.From, run.To, sx);
                if (uCount == 0) continue;

                XYZ pExt = axes.At(uFirst, v, extOffset);
                XYZ pInt = axes.At(uFirst, v, intOffset);
                RebarFactory.CreateSet(_doc, tieStyle, barTypeId, axes.Wall,
                                       axes.LengthDir, new List<Curve> { Line.CreateBound(pExt, pInt) },
                                       uCount, uSpacing, tag, hookId, hookId);
                count += uCount;
            }
        }

        return count;
    }
}
