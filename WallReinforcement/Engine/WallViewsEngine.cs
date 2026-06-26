using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;
using View = Autodesk.Revit.DB.View;

namespace WallReinforcement.Engine;

/// <summary>
/// Per-wall reinforcement documentation: an exterior- and interior-face elevation, a horizontal
/// section through the thickness (the reinforcement "plan"), an optional 3D cage, a rebar schedule
/// and a sheet. Each view is isolated to the wall's own <c>WR:…:{wallId}</c> rebar, shown
/// unobscured. Mirrors SlabViewsEngine, adapted for the vertical geometry of walls. Run inside an
/// open TransactionGroup.
/// </summary>
public sealed class WallViewsEngine
{
    private readonly Document _doc;
    private readonly WallViewsConfig _cfg;
    private readonly HashSet<string> _viewNames;
    private readonly HashSet<string> _sheetNumbers;

    public WallViewsEngine(Document doc, WallViewsConfig cfg)
    {
        _doc = doc;
        _cfg = cfg;
        _viewNames = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate).Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _sheetNumbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Select(s => s.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public ViewRunResult Run(IList<ElementId> wallIds)
    {
        var result = new ViewRunResult();
        foreach (ElementId id in wallIds)
        {
            if (_doc.GetElement(id) is not Wall wall) { result.Error($"Element {id.Value} is not a wall — skipped."); continue; }
            try { ProcessWall(wall, result); }
            catch (Exception ex) { result.Error($"Wall {id.Value} ({MarkOf(wall)}): {ex.Message}"); }
        }
        return result;
    }

    private void ProcessWall(Wall wall, ViewRunResult result)
    {
        WallAxes axes = WallAxes.For(wall);
        string mark = MarkOf(wall);
        if (string.IsNullOrWhiteSpace(mark)) mark = $"Wall-{wall.Id.Value}";

        using var tx = new Transaction(_doc, $"Wall Views — {mark}");
        tx.Start();
        FailureHandlingOptions failOpts = tx.GetFailureHandlingOptions();
        failOpts.SetFailuresPreprocessor(new WarningSwallower());
        tx.SetFailureHandlingOptions(failOpts);

        int scale = _cfg.AutoScale ? PickScale(axes) : _cfg.Scale;
        var views = new List<View>();

        if (_cfg.CreateElevations)
        {
            View? e1 = Make("elevation Exterior", result, () => MakeElevation(axes, mark, "Exterior", scale));
            if (e1 != null) views.Add(e1);
            View? e2 = Make("elevation Interior", result, () => MakeElevation(axes, mark, "Interior", scale));
            if (e2 != null) views.Add(e2);
        }
        if (_cfg.CreateSection)
        {
            View? sec = Make("section", result, () => MakeSection(axes, mark, scale));
            if (sec != null) views.Add(sec);
        }
        if (_cfg.Create3DView)
        {
            View? v3 = Make("3D cage", result, () => Make3D(wall, mark));
            if (v3 != null) views.Add(v3);
        }

        _doc.Regenerate();
        foreach (View v in views) IsolateToWall(v, wall.Id);
        result.ViewsCreated += views.Count;

        AssignScheduleMarks(WallRebar(wall.Id));
        var schedules = new List<ViewSchedule>();
        if (_cfg.CreateSchedule)
        {
            try
            {
                string nm = UniqueName(_cfg.ScheduleNameTemplate.Replace("{Mark}", mark));
                schedules.Add(new WallScheduleBuilder(_doc).BuildRebarSchedule(wall.Id, nm));
                result.SchedulesCreated++;
            }
            catch (Exception ex) { result.Error($"Schedule for {mark}: {ex.Message}"); }
        }

        if (_cfg.PlaceOnSheet)
        {
            try
            {
                var builder = new WallSheetBuilder(_doc, _cfg);
                string number = UniqueSheetNumber(_cfg.SheetNumberTemplate.Replace("{Mark}", mark));
                ViewSheet sheet = builder.CreateSheet(number, _cfg.SheetNameTemplate.Replace("{Mark}", mark));
                builder.PlaceOnSheet(sheet, views, schedules);
                result.SheetsCreated++;
            }
            catch (Exception ex) { result.Error($"Sheet for {mark}: {ex.Message}"); }
        }

        tx.Commit();
    }

    private View? Make(string what, ViewRunResult result, Func<View> create)
    {
        try { return create(); }
        catch (Exception ex) { result.Error($"{what}: {ex.Message}"); return null; }
    }

    /// <summary>
    /// Elevation looking square at one face. Exterior = viewer outside the wall looking along
    /// −Normal; Interior = viewer inside looking along +Normal. The section box spans the full
    /// thickness so the near mesh reads and (with SetUnobscuredInView) the far one shows through.
    /// </summary>
    private View MakeElevation(WallAxes axes, string mark, string face, int scale)
    {
        ViewFamilyType vft = FindVft(ViewFamily.Section, _cfg.SectionViewTypeName);
        bool exterior = face == "Exterior";
        XYZ basisZ = (exterior ? axes.Normal : axes.Normal.Negate()).Normalize();   // toward the viewer
        XYZ basisY = XYZ.BasisZ;                                                     // world up
        XYZ basisX = basisY.CrossProduct(basisZ).Normalize();                       // view "right"
        XYZ center = axes.Origin + axes.LengthDir * (axes.Length / 2.0) + axes.HeightDir * (axes.Height / 2.0);

        double pad = _cfg.CropPadding;
        double hX = axes.Length / 2.0 + pad;
        double hY = axes.Height / 2.0 + pad;
        double depth = axes.Thickness + pad;

        var box = new BoundingBoxXYZ
        {
            Transform = new Transform(Transform.Identity) { Origin = center, BasisX = basisX, BasisY = basisY, BasisZ = basisZ },
            Min = new XYZ(-hX, -hY, -depth),
            Max = new XYZ(hX, hY, pad),
        };

        ViewSection v = ViewSection.CreateSection(_doc, vft.Id, box);
        v.CropBoxActive = true;
        v.CropBoxVisible = false;
        v.Name = UniqueName(_cfg.ElevationNameTemplate.Replace("{Mark}", mark).Replace("{Face}", face));
        ApplyAppearance(v, scale);
        return v;
    }

    /// <summary>Horizontal section at mid-height, looking down — both mats + the transverse ties.</summary>
    private View MakeSection(WallAxes axes, string mark, int scale)
    {
        ViewFamilyType vft = FindVft(ViewFamily.Section, _cfg.SectionViewTypeName);
        XYZ basisZ = XYZ.BasisZ;                                  // looking down → toward viewer is up
        XYZ basisX = axes.LengthDir;                              // wall length across the view
        XYZ basisY = basisZ.CrossProduct(basisX).Normalize();    // thickness up the view
        XYZ center = axes.Origin + axes.LengthDir * (axes.Length / 2.0) + axes.HeightDir * (axes.Height / 2.0);

        double pad = _cfg.CropPadding;
        double hX = axes.Length / 2.0 + pad;
        double hY = axes.Thickness / 2.0 + pad;
        double band = axes.Height / 2.0 + _cfg.SectionDepthFt;   // vertical slice captured by the cut

        var box = new BoundingBoxXYZ
        {
            Transform = new Transform(Transform.Identity) { Origin = center, BasisX = basisX, BasisY = basisY, BasisZ = basisZ },
            Min = new XYZ(-hX, -hY, -band),
            Max = new XYZ(hX, hY, band),
        };

        ViewSection v = ViewSection.CreateSection(_doc, vft.Id, box);
        v.CropBoxActive = true;
        v.CropBoxVisible = false;
        v.Name = UniqueName(_cfg.SectionNameTemplate.Replace("{Mark}", mark));
        ApplyAppearance(v, scale);
        return v;
    }

    private View Make3D(Wall wall, string mark)
    {
        ViewFamilyType vft = FindVft(ViewFamily.ThreeDimensional, null);
        View3D v = View3D.CreateIsometric(_doc, vft.Id);
        BoundingBoxXYZ bb = wall.get_BoundingBox(null);
        double p = _cfg.CropPadding;
        v.SetSectionBox(new BoundingBoxXYZ
        {
            Min = new XYZ(bb.Min.X - p, bb.Min.Y - p, bb.Min.Z - p),
            Max = new XYZ(bb.Max.X + p, bb.Max.Y + p, bb.Max.Z + p),
        });
        v.IsSectionBoxActive = true;
        v.Name = UniqueName(_cfg.View3DNameTemplate.Replace("{Mark}", mark));
        TrySet(() => v.Scale = _cfg.View3DScale);
        TrySet(() => v.DetailLevel = _cfg.DetailLevel);
        return v;
    }

    private void IsolateToWall(View view, ElementId wallId)
    {
        string marker = ":" + wallId.Value;
        List<Element> rebar = new FilteredElementCollector(_doc, view.Id)
            .OfCategory(BuiltInCategory.OST_Rebar).WhereElementIsNotElementType().ToList();
        List<Element> areas = new FilteredElementCollector(_doc, view.Id)
            .OfCategory(BuiltInCategory.OST_AreaRein).WhereElementIsNotElementType().ToList();

        // The AreaReinforcement face mesh's child bars (RebarInSystem) are OST_Rebar but carry NO
        // Comments tag — only the parent AR does. Collect this wall's mesh-bar ids by ownership so
        // the isolate below KEEPS them; otherwise the whole face mesh gets hidden as "untagged".
        var keepMeshBarIds = new HashSet<ElementId>();
        foreach (Element e in areas)
            if (HasWallTag(e, marker) && e is AreaReinforcement ka)
                foreach (ElementId rid in ka.GetRebarInSystemIds())
                    keepMeshBarIds.Add(rid);

        // This wall's bars: unobscured (else bars behind concrete don't draw), middle-bar presentation.
        foreach (Element e in rebar)
            if (HasWallTag(e, marker) && e is Rebar bar)
            {
                try { bar.SetUnobscuredInView(view, true); } catch { /* best-effort */ }
                if (_cfg.ShowMiddleBarOnly)
                    try { if (bar.CanApplyPresentationMode(view)) bar.SetPresentationMode(view, RebarPresentationMode.Middle); }
                    catch { /* best-effort */ }
            }
        foreach (Element e in areas)
            if (HasWallTag(e, marker) && e is AreaReinforcement ar)
                foreach (ElementId rid in ar.GetRebarInSystemIds())
                    if (_doc.GetElement(rid) is RebarInSystem ris)
                        try { ris.SetUnobscuredInView(view, true); } catch { /* best-effort */ }

        if (_cfg.Isolation == RebarIsolation.Show) return;

        List<Element> others = rebar.Where(e => !HasWallTag(e, marker) && !keepMeshBarIds.Contains(e.Id))
            .Concat(areas.Where(e => !HasWallTag(e, marker))).ToList();
        if (others.Count == 0) return;

        if (_cfg.Isolation == RebarIsolation.Halftone)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetHalftone(true);
            foreach (Element e in others) try { view.SetElementOverrides(e.Id, ogs); } catch { /* best-effort */ }
        }
        else
        {
            List<ElementId> ids = others.Where(e => e.CanBeHidden(view)).Select(e => e.Id).ToList();
            if (ids.Count > 0) try { view.HideElements(ids); } catch { /* best-effort */ }
        }
    }

    private static bool HasWallTag(Element e, string marker)
    {
        string? tag = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
        return tag is not null
            && tag.StartsWith("WR:", StringComparison.Ordinal)
            && tag.EndsWith(marker, StringComparison.Ordinal);
    }

    private List<Rebar> WallRebar(ElementId wallId)
    {
        string marker = ":" + wallId.Value;
        return new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Rebar)
            .WhereElementIsNotElementType().OfType<Rebar>()
            .Where(r => HasWallTag(r, marker)).ToList();
    }

    /// <summary>Numbers Schedule Mark 1..N over unique (Type, Shape, Total Length) groups so
    /// identical bars share a mark. Best-effort — skipped if the parameter is read-only.</summary>
    private void AssignScheduleMarks(IReadOnlyList<Rebar> rebar)
    {
        if (rebar.Count == 0) return;
        var groups = rebar
            .Select(r => (Bar: r,
                Type: _doc.GetElement(r.GetTypeId())?.Name ?? "",
                Shape: r.LookupParameter("Shape")?.AsValueString() ?? "",
                Len: r.LookupParameter("Total Bar Length")?.AsDouble() ?? r.LookupParameter("Bar Length")?.AsDouble() ?? 0.0))
            .GroupBy(x => (x.Type, x.Shape, Math.Round(x.Len, 4)))
            .OrderBy(g => g.Key.Item1, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Item2, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Item3)
            .ToList();

        int mark = 0;
        foreach (var group in groups)
        {
            mark++;
            foreach (var x in group)
            {
                Parameter? p = x.Bar.LookupParameter("Schedule Mark");
                if (p is null || p.IsReadOnly) continue;
                if (p.StorageType == StorageType.Integer) p.Set(mark);
                else if (p.StorageType == StorageType.String) p.Set(mark.ToString());
            }
        }
    }

    private int PickScale(WallAxes axes)
    {
        double maxFt = Math.Max(axes.Length, axes.Height);
        double needed = maxFt * 12.0 / Math.Max(1.0, _cfg.TargetViewWidthIn);
        foreach (int s in new[] { 12, 16, 24, 32, 48, 64, 96, 128, 192 })
            if (s >= needed) return s;
        return 192;
    }

    private void ApplyAppearance(View v, int scale)
    {
        TrySet(() => v.Scale = scale);
        TrySet(() => v.DetailLevel = _cfg.DetailLevel);
        TrySet(() => v.DisplayStyle = _cfg.VisualStyle);
        ApplyTemplate(v);
    }

    private void ApplyTemplate(View view)
    {
        if (string.IsNullOrWhiteSpace(_cfg.ViewTemplateName)) return;
        View? template = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(v => v.IsTemplate && v.ViewType == view.ViewType
                && string.Equals(v.Name, _cfg.ViewTemplateName, StringComparison.OrdinalIgnoreCase));
        if (template is not null) view.ViewTemplateId = template.Id;
    }

    private ViewFamilyType FindVft(ViewFamily family, string? preferred)
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
        if (_viewNames.Add(desired)) return desired;
        for (int i = 1; i <= 999; i++) { string c = $"{desired}_{i}"; if (_viewNames.Add(c)) return c; }
        string f = $"{desired}_{Guid.NewGuid().ToString("N")[..6]}"; _viewNames.Add(f); return f;
    }

    private string UniqueSheetNumber(string desired)
    {
        if (_sheetNumbers.Add(desired)) return desired;
        for (int i = 1; i <= 999; i++) { string c = $"{desired}-{i}"; if (_sheetNumbers.Add(c)) return c; }
        string f = $"{desired}-{Guid.NewGuid().ToString("N")[..6]}"; _sheetNumbers.Add(f); return f;
    }

    private static void TrySet(Action set)
    {
        try { set(); }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
    }

    private string MarkOf(Element e) => e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
}
