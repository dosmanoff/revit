using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// Places transverse ties (stirrups across the wall thickness) at a grid spacing.
/// Skipped for walls thinner than <see cref="TiesConfig.MinThickness"/>.
///
/// A tie is a single straight bar in the section plane (perpendicular to the wall length)
/// running from the exterior-face cover line to the interior-face cover line. For monolithic
/// walls a simple "open tie" is the common detail and what we emit here; closed loops belong
/// to Phase 5 if the bend geometry is needed.
/// </summary>
public class TransverseTieBuilder
{
    private readonly Document _doc;

    public TransverseTieBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, string tag)
    {
        TiesConfig ties = cfg.Ties;
        if (!ties.Enabled) return 0;
        if (axes.Thickness < cfg.Ft(ties.MinThickness)) return 0;

        ElementId barTypeId = RebarFactory.LookupBarType(_doc, ties.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double endsCover   = cfg.Ft(cfg.Cover.Ends);
        double topCover    = cfg.Ft(cfg.Cover.Top);
        double bottomCover = cfg.Ft(cfg.Cover.Bottom);
        double extOffset   =  axes.HalfThickness - cfg.Ft(cfg.Cover.Exterior);
        double intOffset   = -axes.HalfThickness + cfg.Ft(cfg.Cover.Interior);
        double sx          = cfg.Ft(ties.SpacingX);
        double sy          = cfg.Ft(ties.SpacingY);

        var (uCount, uSpacing, uFirst) = RebarFactory.UniformLayout(endsCover, axes.Length - endsCover, sx);
        if (uCount == 0) return 0;

        int count = 0;
        // One rebar SET per height row, each distributed along the wall length — turns an
        // N×M grid of loose bars into M set elements (orders of magnitude fewer API calls).
        foreach (double v in RebarFactory.EvenlySpaced(bottomCover, axes.Height - topCover, sy))
        {
            XYZ pExt = axes.At(uFirst, v, extOffset);
            XYZ pInt = axes.At(uFirst, v, intOffset);

            // A transverse crosstie is a single straight bar across the thickness. Use Standard
            // style (a 1-segment StirrupTie is rejected at regeneration — it expects a closed/
            // hooked shape).
            RebarFactory.CreateSet(_doc, RebarStyle.Standard, barTypeId, axes.Wall,
                                   axes.LengthDir, new List<Curve> { Line.CreateBound(pExt, pInt) },
                                   uCount, uSpacing, tag);
            count += uCount;
        }

        return count;
    }
}
