using Autodesk.Revit.DB;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Geometry;
using View = Autodesk.Revit.DB.View;

namespace StairsReinforcement.Engine;

/// <summary>
/// Per stair, creates a cropped structural plan, one longitudinal section per distinct flight direction
/// (a second section is added for a flight that isn't parallel to the first — e.g. an L-stair), a rebar
/// schedule, annotations, and a sheet. On every view it hides foreign rebar + foreign view-reference
/// markers, draws this stair's bars unobscured, and sets multi-bar sets to First/Last. Must run inside
/// an open TransactionGroup.
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

        var views = new List<View>();

        // Plan first (lands top-left on the sheet).
        if (_cfg.CreatePlan)
        {
            try
            {
                ViewPlan plan = new StairPlanBuilder(_doc, _cfg)
                    .Create(min, max, UniqueName(Token(_cfg.PlanNameTemplate, mark)));
                ApplyTemplateNamed(plan, _cfg.PlanViewTemplateName);
                ApplyRebarDisplay(plan, asm.Id.Value);
                HideForeignViewMarkers(plan, mark);
                views.Add(plan);
                result.ViewsCreated++;
            }
            catch (Exception ex) { result.Error($"Plan for {mark}: {ex.Message}"); }
        }

        // One section per distinct flight direction (parallel flights share one; an L-stair adds a second).
        ViewFamilyType vft = FindViewFamilyType(ViewFamily.Section, _cfg.SectionViewTypeName);
        List<(XYZ right, XYZ min, XYZ max)> placements = SectionPlacements(asm, min, max);
        for (int i = 0; i < placements.Count; i++)
        {
            (XYZ right, XYZ smin, XYZ smax) = placements[i];
            string template = i == 0 ? _cfg.ViewNameTemplate : _cfg.SecondSectionNameTemplate;
            ViewSection section = ViewSection.CreateSection(_doc, vft.Id, SectionBox(right, smin, smax));
            section.Name = UniqueName(Token(template, mark));
            ApplyAppearance(section);
            ApplyTemplate(section);
            ApplyRebarDisplay(section, asm.Id.Value);
            HideForeignViewMarkers(section, mark);
            views.Add(section);
            result.ViewsCreated++;
        }

        Annotate(views, asm.Id.Value, result);

        List<ViewSchedule> schedules = BuildSchedule(mark, result);
        if (_cfg.PlaceOnSheet) BuildSheet(mark, views, schedules, result);

        tx.Commit();
    }

    /// <summary>
    /// One section placement per distinct horizontal flight direction. Flights whose plan run-axes are
    /// collinear (within the parallel tolerance, sign-independent — a switchback's anti-parallel flights
    /// count as one line) share the primary section over the whole stair. A flight on a different axis
    /// (an L-stair's second run) gets its own section, cropped to that flight + the landings.
    /// </summary>
    private List<(XYZ right, XYZ min, XYZ max)> SectionPlacements(StairAssembly asm, XYZ allMin, XYZ allMax)
    {
        var groups = new List<(XYZ dir, List<FlightComponent> flights)>();
        double tol = Math.Cos(Math.Max(1.0, _cfg.ParallelToleranceDeg) * Math.PI / 180.0);
        foreach (FlightComponent f in asm.Flights)
        {
            var d = new XYZ(f.Frame.U.X, f.Frame.U.Y, 0);
            if (d.IsZeroLength()) continue;
            d = d.Normalize();
            int gi = groups.FindIndex(g => Math.Abs(g.dir.DotProduct(d)) >= tol);
            if (gi < 0) groups.Add((d, new List<FlightComponent> { f }));
            else groups[gi].flights.Add(f);
        }

        var result = new List<(XYZ, XYZ, XYZ)>();
        if (groups.Count == 0) { result.Add((XYZ.BasisX, allMin, allMax)); return result; }

        result.Add((groups[0].dir, allMin, allMax));                 // primary, over the whole stair
        if (_cfg.SecondSectionWhenNotParallel)
            for (int i = 1; i < groups.Count; i++)
            {
                (XYZ gmin, XYZ gmax) = GroupBounds(groups[i].flights, asm.Landings);
                result.Add((groups[i].dir, gmin, gmax));
            }
        return result;
    }

    private static (XYZ min, XYZ max) GroupBounds(IEnumerable<FlightComponent> flights, IEnumerable<LandingComponent> landings)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        void Add(Bounds3 b)
        {
            if (b.IsEmpty) return;
            minX = Math.Min(minX, b.Min.X); minY = Math.Min(minY, b.Min.Y); minZ = Math.Min(minZ, b.Min.Z);
            maxX = Math.Max(maxX, b.Max.X); maxY = Math.Max(maxY, b.Max.Y); maxZ = Math.Max(maxZ, b.Max.Z);
        }
        foreach (FlightComponent f in flights) Add(f.Bounds);
        foreach (LandingComponent l in landings) Add(l.Bounds);
        return (new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
    }

    /// <summary>Section box oriented along <paramref name="right"/> (the run), world Z up, looking across.</summary>
    private BoundingBoxXYZ SectionBox(XYZ right, XYZ min, XYZ max)
    {
        XYZ center = (min + max) * 0.5;
        right = right.IsZeroLength() ? XYZ.BasisX : right.Normalize();
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

    private void Annotate(IReadOnlyList<View> views, long stairId, ViewRunResult result)
    {
        if (!_cfg.CreateTags && !_cfg.CreateSpacingAnnotations) return;
        var builder = new StairAnnotationBuilder(_doc, _cfg);
        foreach (View v in views)
        {
            try { result.AnnotationsCreated += builder.Annotate(v, stairId); }
            catch (Exception ex) { result.Error($"Annotations in {v.Name}: {ex.Message}"); }
        }
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

    /// <summary>
    /// Config-driven rebar display: hide every rebar whose <c>Comments</c> tag isn't
    /// <c>STR:{config}:{stairId}:…</c>, and for this stair's own bars draw them unobscured and set multi-bar
    /// sets to First/Last. Element-level hide also wins over any view-template filters.
    /// </summary>
    private void ApplyRebarDisplay(View view, long stairId)
    {
        string id = stairId.ToString();
        var hide = new List<ElementId>();
        foreach (Element e in new FilteredElementCollector(_doc, view.Id)
                     .OfCategory(BuiltInCategory.OST_Rebar).WhereElementIsNotElementType())
        {
            string[] p = (e.LookupParameter("Comments")?.AsString() ?? "").Split(':');
            bool mine = p.Length >= 3 && p[0] == "STR" && p[2] == id;
            if (mine)
            {
                if (e is Autodesk.Revit.DB.Structure.Rebar rb)
                {
                    if (_cfg.OwnRebarUnobscured) try { rb.SetUnobscuredInView(view, true); } catch { }
                    if (_cfg.RebarFirstLast)
                        try { rb.SetPresentationMode(view, Autodesk.Revit.DB.Structure.RebarPresentationMode.FirstLast); } catch { }
                }
            }
            else if (_cfg.HideForeignRebar) hide.Add(e.Id);
        }
        if (hide.Count > 0)
            try { view.HideElements(hide); } catch { }
    }

    /// <summary>
    /// Hide foreign view-reference markers so only this stair's own section/plan markers can show on the
    /// view (the user's rule for the stair sheets). These markers are category <c>OST_Views</c> reference
    /// <see cref="Element"/>s — NOT <see cref="ViewSection"/> instances — so a ViewSection/category quick
    /// filter misses them (it returns empty in a view-scoped collector); instead collect everything drawn
    /// in the view and match the category by id. A marker whose name starts with the stair's mark (its own
    /// views) is kept; the rest — other stairs' sections, wall/column sections, callouts — are hidden.
    /// Markers for stairs generated AFTER this one can't be hidden here (their views don't exist yet);
    /// a re-run catches those.
    /// </summary>
    private void HideForeignViewMarkers(View view, string mark)
    {
        _doc.Regenerate();                               // populate the view's annotation markers first
        var hide = new List<ElementId>();
        foreach (Element e in new FilteredElementCollector(_doc, view.Id).WhereElementIsNotElementType())
        {
            if (e.Category is null || e.Category.Id.Value != (long)BuiltInCategory.OST_Views) continue;
            if (e.Id.Value == view.Id.Value) continue;   // never the host view itself
            string n; try { n = e.Name ?? ""; } catch { n = ""; }
            if (n.StartsWith(mark, StringComparison.OrdinalIgnoreCase)) continue;   // this stair's own views
            hide.Add(e.Id);
        }
        foreach (ElementId id in hide)
            try { view.HideElements(new List<ElementId> { id }); } catch { }
    }

    private void ApplyAppearance(View view)
    {
        TrySet(() => view.Scale = _cfg.ViewScale);
        TrySet(() => view.DetailLevel = _cfg.DetailLevel);
        TrySet(() => view.DisplayStyle = _cfg.VisualStyle);
    }

    private void ApplyTemplate(View view) => ApplyTemplateNamed(view, _cfg.ViewTemplateName);

    private void ApplyTemplateNamed(View view, string? templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName)) return;
        View? template = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(v => v.IsTemplate && v.ViewType == view.ViewType
                && string.Equals(v.Name, templateName, StringComparison.OrdinalIgnoreCase));
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
