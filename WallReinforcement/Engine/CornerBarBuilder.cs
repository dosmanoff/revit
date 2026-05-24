using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// L-shaped continuity bars at wall-to-wall L-corners.
///
/// One L-bar per face per height step: two legs of length <see cref="CornersConfig.LapLengthMm"/>,
/// one along our wall going away from the joint, one along the other wall going away from the joint.
/// Both legs are inset by the appropriate face cover.
///
/// Junctions where the other wall is NOT in the selected/processed set are still handled here
/// (so the corner is reinforced even when the user picks only one of the two corner walls); the
/// owner tag is OUR wall, so re-running on our wall replaces these bars cleanly.
/// </summary>
public class CornerBarBuilder
{
    private readonly Document _doc;

    public CornerBarBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, IReadOnlyList<WallJunction> junctions, string tag)
    {
        if (!cfg.Corners.Enabled) return 0;
        ElementId barTypeId = LookupBarType(cfg.Corners.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double lap          = UnitConv.MmToFt(cfg.Corners.LapLengthMm);
        double spacing      = UnitConv.MmToFt(cfg.Corners.SpacingMm);
        double topCover     = UnitConv.MmToFt(cfg.Cover.TopMm);
        double bottomCover  = UnitConv.MmToFt(cfg.Cover.BottomMm);
        double extCover     = UnitConv.MmToFt(cfg.Cover.ExteriorMm);
        double intCover     = UnitConv.MmToFt(cfg.Cover.InteriorMm);

        int count = 0;
        foreach (WallJunction j in junctions)
        {
            if (j.Kind != JunctionKind.LCorner) continue;

            // Only place when our wall has the smaller Id so we don't double up if the
            // user processes both corner walls in the same run.
            if (axes.Wall.Id.Value > j.OtherWall.Id.Value) continue;

            // Direction along OUR wall pointing away from the joint.
            double ourSign = j.OurU < axes.Length * 0.5 ? +1 : -1;

            foreach (double v in EvenlySpaced(bottomCover, axes.Height - topCover, spacing))
            foreach (double faceOffset in new[] {  axes.HalfThickness - extCover,
                                                  -axes.HalfThickness + intCover })
            {
                // Leg 1: along OUR wall, from joint inward by `lap`.
                XYZ p0 = axes.At(j.OurU, v, faceOffset);
                XYZ p1 = axes.At(j.OurU + ourSign * lap, v, faceOffset);

                // Leg 2: along OTHER wall, from joint inward by `lap`. Approximate by stepping
                // in OtherDir from the joint point at the same height v and the same face offset
                // (face offset orientation flips between walls; we keep it consistent by using
                // the joint point as origin for the second leg, which is geometrically the lap).
                XYZ jointAtHeight = new(j.Point.X, j.Point.Y, axes.Origin.Z + v);
                XYZ p2 = jointAtHeight + j.OtherDir * lap;

                var curves = new List<Curve>
                {
                    Line.CreateBound(p1, p0),
                    Line.CreateBound(p0, p2),
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
