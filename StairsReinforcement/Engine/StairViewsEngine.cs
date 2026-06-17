using Autodesk.Revit.DB;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Geometry;
using View = Autodesk.Revit.DB.View;

namespace StairsReinforcement.Engine;

/// <summary>
/// Creates a longitudinal section per stair (oriented along the run, looking across the width),
/// cropped to the stair, plus a rebar schedule and a sheet. Must run inside an open
/// TransactionGroup. Adapts SlabViewsEngine (plan views) to vertical section views.
/// </summary>
public sealed class StairViewsEngine
{
    private readonly Document _doc;
    private readonly StairViewsConfig _cfg;
    private readonly HashSet<string> _existingViewNames;
    private readonly HashSet<string> _existingSheetNumbers;

    public StairViewsEngine(Document doc, StairViewsConfig cfg)
    {
        _doc = doc;
        _cfg = cfg;
        _existingViewNames = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate).Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _existingSheetNumbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Select(s => s.SheetNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public ViewRunResult Run(IReadOnlyList<StairAssembly> assemblies)
    {
        var result = new ViewRunResult();
        foreach (StairAssembly asm in assemblies)
        {
            try { ProcessStair(asm, result); }
            catch (Exception ex) { result.Error($"Stair {asm.Id.Value} ({asm.Mark ?? "—"}): {ex.Message}"); }
        }
        return result;
    }

    private void ProcessStair(StairAssembly asm, ViewRunResult result)
    {
        if (!TryOverallBounds(asm, out XYZ min, out XYZ max))
        {
            result.Error($"Stair {asm.Id.Value} has no geometry — skipped.");
            return;
        }

        string mark = string.IsNullOrWhiteSpace(asm.Mark) ? $"STR-{asm.Id.Value}" : asm.Mark!;

        using var tx = new Transaction(_doc, $"Stair Views — {mark}");
        tx.Start();

        ViewFamilyType vft = FindViewFamilyType(ViewFamily.Section, _cfg.SectionViewTypeName);
        ViewSection section = ViewSection.CreateSection(_doc, vft.Id, SectionBox(asm, min, max));
        NameView(section, mark);
        ApplyAppearance(section);
        ApplyTemplate(section);
        result.ViewsCreated++;

        var views = new List<View> { section };
        List<ViewSchedule> schedules = BuildSchedule(mark, result);
        if (_cfg.PlaceOnSheet) BuildSheet(mark, views, schedules, result);

        tx.Commit();
    }

    /// <summary>Section oriented along the run (X), world Z up (Y), looking across the width (Z).</summary>
    private BoundingBoxXYZ SectionBox(StairAssembly asm, XYZ min, XYZ max)
    {
        XYZ center = (min + max) * 0.5;

        XYZ right = RunDirection(asm);
        XYZ up = XYZ.BasisZ;
        XYZ viewDir = right.CrossProduct(up).Normalize();   // horizontal, across the width

        double rMin = double.MaxValue, rMax = double.MinValue;
        double uMin = double.MaxValue, uMax = double.MinValue;
        double dMin = double.MaxValue, dMax = double.MinValue;
        foreach (XYZ corner in Corners(min, max))
        {
            XYZ rel = corner - center;
            double r = rel.DotProduct(right), u = rel.DotProduct(up), d = rel.DotProduct(viewDir);
            rMin = Math.Min(rMin, r); rMax = Math.Max(rMax, r);
            uMin = Math.Min(uMin, u); uMax = Math.Max(uMax, u);
            dMin = Math.Min(dMin, d); dMax = Math.Max(dMax, d);
        }

        double pad = _cfg.CropPadding;
        var t = Transform.Identity;
        t.Origin = center;
        t.BasisX = right;
        t.BasisY = up;
        t.BasisZ = viewDir;

        return new BoundingBoxXYZ
        {
            Transform = t,
            Min = new XYZ(rMin - pad, uMin - pad, dMin - pad),
            Max = new XYZ(rMax + pad, uMax + pad, dMax + pad),
        };
    }

    private static XYZ RunDirection(StairAssembly asm)
    {
        FlightComponent? f = asm.Flights.FirstOrDefault();
        if (f is not null)
        {
            var u = new XYZ(f.Frame.U.X, f.Frame.U.Y, 0);
            if (!u.IsZeroLength()) return u.Normalize();
        }
        return XYZ.BasisX;
    }

    private static IEnumerable<XYZ> Corners(XYZ min, XYZ max)
    {
        foreach (double x in new[] { min.X, max.X })
            foreach (double y in new[] { min.Y, max.Y })
                foreach (double z in new[] { min.Z, max.Z })
                    yield return new XYZ(x, y, z);
    }

    private static bool TryOverallBounds(StairAssembly asm, out XYZ min, out XYZ max)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        bool any = false;

        void Add(Bounds3 b)
        {
            if (b.IsEmpty) return;
            any = true;
            minX = Math.Min(minX, b.Min.X); minY = Math.Min(minY, b.Min.Y); minZ = Math.Min(minZ, b.Min.Z);
            maxX = Math.Max(maxX, b.Max.X); maxY = Math.Max(maxY, b.Max.Y); maxZ = Math.Max(maxZ, b.Max.Z);
        }

        foreach (FlightComponent f in asm.Flights) Add(f.Bounds);
        foreach (LandingComponent l in asm.Landings) Add(l.Bounds);

        min = new XYZ(minX, minY, minZ);
        max = new XYZ(maxX, maxY, maxZ);
        return any;
    }

    private List<ViewSchedule> BuildSchedule(string mark, ViewRunResult result)
    {
        var list = new List<ViewSchedule>();
        if (!_cfg.CreateSchedule) return list;
        try
        {
            string name = UniqueName(Token(_cfg.ScheduleNameTemplate, mark));
            list.Add(new StairScheduleBuilder(_doc).BuildRebarSchedule(mark, name));
            result.SchedulesCreated++;
        }
        catch (Exception ex) { result.Error($"Schedule for {mark}: {ex.Message}"); }
        return list;
    }

    private void BuildSheet(string mark, IReadOnlyList<View> views, IReadOnlyList<ViewSchedule> schedules, ViewRunResult result)
    {
        try
        {
            var builder = new StairSheetBuilder(_doc, _cfg);
            string number = UniqueSheetNumber(Token(_cfg.SheetNumberTemplate, mark));
            string name = Token(_cfg.SheetNameTemplate, mark);
            ViewSheet sheet = builder.CreateSheet(number, name);
            builder.PlaceOnSheet(sheet, views, schedules);
            result.SheetsCreated++;
        }
        catch (Exception ex) { result.Error($"Sheet for {mark}: {ex.Message}"); }
    }

    private static string Token(string template, string mark) => template.Replace("{Mark}", mark);

    private void NameView(View view, string mark)
    {
        string name = _cfg.ViewNameTemplate.Replace("{Mark}", mark).Trim();
        if (string.IsNullOrEmpty(name)) name = $"View {view.Id.Value}";
        view.Name = UniqueName(name);
    }

    private void ApplyAppearance(View view)
    {
        TrySet(() => view.Scale = _cfg.ViewScale);
        TrySet(() => view.DetailLevel = _cfg.DetailLevel);
        TrySet(() => view.DisplayStyle = _cfg.VisualStyle);
    }

    private void ApplyTemplate(View view)
    {
        if (string.IsNullOrWhiteSpace(_cfg.ViewTemplateName)) return;
        View? template = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(v => v.IsTemplate && v.ViewType == view.ViewType
                && string.Equals(v.Name, _cfg.ViewTemplateName, StringComparison.OrdinalIgnoreCase));
        if (template is not null) view.ViewTemplateId = template.Id;
    }

    private ViewFamilyType FindViewFamilyType(ViewFamily family, string? preferred)
    {
        List<ViewFamilyType> candidates = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .Where(t => t.ViewFamily == family).ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException($"No ViewFamilyType for ViewFamily.{family}.");

        if (preferred is not null)
        {
            ViewFamilyType? hit = candidates.FirstOrDefault(t => string.Equals(t.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return candidates[0];
    }

    private string UniqueName(string desired)
    {
        if (_existingViewNames.Add(desired)) return desired;
        for (int i = 1; i <= 999; i++)
        {
            string candidate = $"{desired}_{i}";
            if (_existingViewNames.Add(candidate)) return candidate;
        }
        string fallback = $"{desired}_{Guid.NewGuid().ToString("N")[..6]}";
        _existingViewNames.Add(fallback);
        return fallback;
    }

    private string UniqueSheetNumber(string desired)
    {
        if (_existingSheetNumbers.Add(desired)) return desired;
        for (int i = 1; i <= 999; i++)
        {
            string candidate = $"{desired}-{i}";
            if (_existingSheetNumbers.Add(candidate)) return candidate;
        }
        string fallback = $"{desired}-{Guid.NewGuid().ToString("N")[..6]}";
        _existingSheetNumbers.Add(fallback);
        return fallback;
    }

    private static void TrySet(Action set)
    {
        try { set(); }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
    }
}
