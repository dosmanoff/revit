using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// For each opening whose strip-above is too short for separate U-bars (see
/// <see cref="WallReinforcement.Geometry.OpeningTopMerge"/>), places one CLOSED stirrup ("хомут")
/// spanning that strip in place of the opening-top U-bar, the wall-top U-bar and the vertical bar
/// between them (which the other builders suppress over the same opening). The loop sits in the
/// (height × thickness) plane — opening top to wall-top cover, across the thickness — and is
/// distributed along the opening width. It stays closed inside the wall (no upward projection).
/// </summary>
public class OpeningTopStirrupBuilder
{
    private readonly Document _doc;

    public OpeningTopStirrupBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, WallLayering lay,
                     ISet<long> mergeOpeningIds, string tag)
    {
        if (!cfg.Openings.MergeTopStirrup || mergeOpeningIds.Count == 0) return 0;
        ElementId barTypeId = RebarFactory.LookupBarType(_doc, cfg.Openings.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        ElementId hookId = RebarFactory.LookupHookType(_doc, "Stirrup/Tie - 135", "Stirrup/Tie - 90", "Stirrup/Tie");
        RebarStyle style = hookId != ElementId.InvalidElementId ? RebarStyle.StirrupTie : RebarStyle.Standard;

        double topV    = axes.Height - cfg.Ft(cfg.Cover.Top);
        double extOff  = lay.TieFace(true);
        double intOff  = lay.TieFace(false);
        double spacing = cfg.Ft(cfg.FaceMesh.Exterior?.Horizontal.Spacing ?? new Length(200));

        int count = 0;
        foreach (OpeningRect o in WallGeometry.GetOpenings(axes))
        {
            if (!mergeOpeningIds.Contains(o.InsertId.Value)) continue;
            var (n, step, first) = RebarFactory.UniformLayout(o.UMin, o.UMax, spacing);
            if (n == 0) continue;

            // Closed rectangular loop at each u: opening-top → wall-top, across the thickness.
            XYZ p1 = axes.At(first, o.VMax, extOff);
            XYZ p2 = axes.At(first, topV,   extOff);
            XYZ p3 = axes.At(first, topV,   intOff);
            XYZ p4 = axes.At(first, o.VMax, intOff);
            var curves = new List<Curve>
            {
                Line.CreateBound(p1, p2), Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4), Line.CreateBound(p4, p1),
            };
            RebarFactory.CreateSet(_doc, style, barTypeId, axes.Wall, axes.LengthDir,
                                   curves, n, step, tag, hookId, hookId);
            count += n;
        }
        return count;
    }
}
