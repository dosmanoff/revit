using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// Places transverse ties (stirrups across the wall thickness) at a grid spacing.
/// Skipped for walls thinner than <see cref="TiesConfig.MinThicknessMm"/>.
///
/// A tie is a closed rectangular loop in the section plane (perpendicular to the wall length):
/// width = wallThickness - cover_ext - cover_int, height = a small constant (the loop height
/// barely matters — Revit treats it as a stirrup with one segment per side).
/// For monolithic walls a simple U-shape ("open tie") is usually enough; we use that here.
/// </summary>
public class TransverseTieBuilder
{
    private readonly Document _doc;

    public TransverseTieBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, string tag)
    {
        TiesConfig ties = cfg.Ties;
        if (!ties.Enabled) return 0;
        if (axes.Thickness < UnitConv.MmToFt(ties.MinThicknessMm)) return 0;

        ElementId barTypeId = LookupBarType(ties.BarType);
        if (barTypeId == ElementId.InvalidElementId) return 0;

        double endsCover   = UnitConv.MmToFt(cfg.Cover.EndsMm);
        double topCover    = UnitConv.MmToFt(cfg.Cover.TopMm);
        double bottomCover = UnitConv.MmToFt(cfg.Cover.BottomMm);
        double extCover    = UnitConv.MmToFt(cfg.Cover.ExteriorMm);
        double intCover    = UnitConv.MmToFt(cfg.Cover.InteriorMm);
        double sx          = UnitConv.MmToFt(ties.SpacingXMm);
        double sy          = UnitConv.MmToFt(ties.SpacingYMm);

        double extOffset =  axes.HalfThickness - extCover;
        double intOffset = -axes.HalfThickness + intCover;

        int count = 0;
        foreach (double u in EvenlySpaced(endsCover, axes.Length - endsCover, sx))
        foreach (double v in EvenlySpaced(bottomCover, axes.Height - topCover, sy))
        {
            // Two points across the wall thickness at (u, v); a single straight tie connecting them.
            XYZ pExt = axes.At(u, v, extOffset);
            XYZ pInt = axes.At(u, v, intOffset);

            var curves = new List<Curve> { Line.CreateBound(pExt, pInt) };

            Rebar rebar = Rebar.CreateFromCurves(
                _doc,
                RebarStyle.StirrupTie,
                (RebarBarType)_doc.GetElement(barTypeId),
                startHook:        null,
                endHook:          null,
                host:             axes.Wall,
                norm:             axes.LengthDir,
                curves:           curves,
                startHookOrient:  RebarHookOrientation.Right,
                endHookOrient:    RebarHookOrientation.Right,
                useExistingShapeIfPossible: true,
                createNewShape:   true);

            ExistingRebarCleaner.Tag(rebar, tag);
            count++;
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
