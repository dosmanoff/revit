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

        int count = 0;
        foreach (double u in RebarFactory.EvenlySpaced(endsCover, axes.Length - endsCover, sx))
        foreach (double v in RebarFactory.EvenlySpaced(bottomCover, axes.Height - topCover, sy))
        {
            XYZ pExt = axes.At(u, v, extOffset);
            XYZ pInt = axes.At(u, v, intOffset);

            RebarFactory.Create(_doc, RebarStyle.StirrupTie, barTypeId, axes.Wall,
                                axes.LengthDir, new List<Curve> { Line.CreateBound(pExt, pInt) }, tag);
            count++;
        }

        return count;
    }
}
