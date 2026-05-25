using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// L-shaped lap bars at T-junctions where OUR wall is the stem terminating on the through wall.
///
/// One L-bar per face per height step: one leg along our wall going away from the joint
/// (length <see cref="TJunctionsConfig.LapLengthMm"/>), one leg along the through wall going
/// in one direction (alternating direction at each height step gives bidirectional development
/// — for simplicity Phase 3 picks one direction; flipping is left for Phase 5).
/// </summary>
public class TJunctionBarBuilder
{
    private readonly Document _doc;

    public TJunctionBarBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, IReadOnlyList<WallJunction> junctions, string tag)
    {
        if (!cfg.TJunctions.Enabled) return 0;
        ElementId barTypeId = LookupBarType(cfg.TJunctions.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double lap         = UnitConv.MmToFt(cfg.TJunctions.LapLengthMm);
        double spacing     = UnitConv.MmToFt(cfg.TJunctions.SpacingMm);
        double topCover    = UnitConv.MmToFt(cfg.Cover.TopMm);
        double bottomCover = UnitConv.MmToFt(cfg.Cover.BottomMm);
        double extCover    = UnitConv.MmToFt(cfg.Cover.ExteriorMm);
        double intCover    = UnitConv.MmToFt(cfg.Cover.InteriorMm);

        int count = 0;
        int heightStep = 0;
        foreach (WallJunction j in junctions)
        {
            if (j.Kind != JunctionKind.TStem) continue;

            double ourSign = j.OurU < axes.Length * 0.5 ? +1 : -1;

            foreach (double v in EvenlySpaced(bottomCover, axes.Height - topCover, spacing))
            foreach (double faceOffset in new[] {  axes.HalfThickness - extCover,
                                                  -axes.HalfThickness + intCover })
            {
                heightStep++;
                XYZ p0 = axes.At(j.OurU, v, faceOffset);
                XYZ pStem = axes.At(j.OurU + ourSign * lap, v, faceOffset);

                // Direction along the through wall — alternate sides to avoid all bars piling on
                // the same side. For monolithic walls this approximates the typical "fan" pattern.
                double thruSign = heightStep % 2 == 0 ? +1 : -1;
                XYZ jointAtHeight = new(j.Point.X, j.Point.Y, axes.Origin.Z + v);
                XYZ pThru = jointAtHeight + j.OtherDir * (thruSign * lap);

                var curves = new List<Curve>
                {
                    Line.CreateBound(pStem, p0),
                    Line.CreateBound(p0, pThru),
                };

                Rebar rebar = Rebar.CreateFromCurves(
                    _doc,
                    RebarStyle.Standard,
                    (RebarBarType)_doc.GetElement(barTypeId),
                    startHook:        null,
                    endHook:          null,
                    host:             axes.Wall,
                    norm:             axes.HeightDir,
                    curves:           curves,
                    startHookOrient:  RebarHookOrientation.Right,
                    endHookOrient:    RebarHookOrientation.Right,
                    useExistingShapeIfPossible: true,
                    createNewShape:   true);

                ExistingRebarCleaner.Tag(rebar, tag);
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<double> EvenlySpaced(double from, double to, double step)
    {
        if (to <= from || step <= 0) yield break;
        double span = to - from;
        int n = Math.Max(1, (int)Math.Ceiling(span / step));
        double actualStep = span / n;
        for (int i = 0; i <= n; i++) yield return from + i * actualStep;
    }

    private ElementId LookupBarType(string name)
    {
        var hit = new FilteredElementCollector(_doc)
            .OfClass(typeof(RebarBarType))
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        return hit?.Id ?? ElementId.InvalidElementId;
    }
}
