using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using View = Autodesk.Revit.DB.View;

namespace SlabReinforcement.Engine;

/// <summary>
/// Creates four layer plan views per slab — Layer 1 Bottom X, 2 Bottom Y, 3 Top X, 4 Top Y —
/// cropped to the slab, with each view isolated to the bars of its layer via the SR: tag.
/// Must run inside an open TransactionGroup. Schedules and sheets are added in PR-14.
/// </summary>
public sealed class SlabViewsEngine
{
    private readonly Document _doc;
    private readonly SlabViewsConfig _cfg;
    private readonly HashSet<string> _existingViewNames;
    private readonly HashSet<string> _existingSheetNumbers;

    private static readonly (SlabLayer Layer, int Num, string Label)[] LayerSet =
    {
        (SlabLayer.BottomX, 1, "Bottom X"),
        (SlabLayer.BottomY, 2, "Bottom Y"),
        (SlabLayer.TopX, 3, "Top X"),
        (SlabLayer.TopY, 4, "Top Y"),
    };

    public SlabViewsEngine(Document doc, SlabViewsConfig cfg)
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

    public ViewRunResult Run(IList<ElementId> slabIds)
    {
        var result = new ViewRunResult();
        foreach (ElementId id in slabIds)
        {
            if (_doc.GetElement(id) is not Floor floor)
            {
                result.Error($"Element {id.Value} is not a floor — skipped.");
                continue;
            }
            try { ProcessSlab(floor, result); }
            catch (Exception ex) { result.Error($"Slab {id.Value} ({MarkOf(floor)}): {ex.Message}"); }
        }
        return result;
    }

    private void ProcessSlab(Floor floor, ViewRunResult result)
    {
        BoundingBoxXYZ? bbox = floor.get_BoundingBox(null);
        if (bbox is null) { result.Error($"Slab {floor.Id.Value} has no bounding box — skipped."); return; }

        Level? level = floor.LevelId != ElementId.InvalidElementId ? _doc.GetElement(floor.LevelId) as Level : null;
        if (level is null) { result.Error($"Slab {floor.Id.Value} has no level — skipped."); return; }

        string mark = MarkOf(floor);

        using var tx = new Transaction(_doc, $"Slab Views — {mark}");
        tx.Start();

        ViewFamilyType vft = FindViewFamilyType(ViewFamily.FloorPlan, _cfg.PlanViewTypeName);
        var created = new List<(View View, SlabLayer Layer)>();

        foreach ((SlabLayer layer, int num, string label) in LayerSet)
        {
            ViewPlan plan = ViewPlan.Create(_doc, vft.Id, level.Id);
            CropAndRange(plan, bbox, level);
            NameView(plan, mark, num, label);
            ApplyAppearance(plan);
            ApplyTemplate(plan);
            created.Add((plan, layer));
        }

        _doc.Regenerate();
        foreach ((View view, SlabLayer layer) in created)
        {
            IsolateLayer(view, floor.Id, layer);
            result.ViewsCreated++;
        }

        var sheetViews = created.Select(c => c.View).ToList();

        if (_cfg.Create3DView && Create3D(floor, bbox, mark, result) is { } v3d)
        {
            sheetViews.Add(v3d);
            result.ViewsCreated++;
        }
        if (_cfg.CreateBendingDetails && CreateBendingDetailView(floor, mark, result) is { } bd)
        {
            sheetViews.Add(bd);
            result.ViewsCreated++;
        }
        if (_cfg.CreateSections)
            sheetViews.AddRange(CreateSections(floor, bbox, mark, result));

        List<ViewSchedule> schedules = BuildSchedule(mark, result);
        if (_cfg.PlaceOnSheet)
            BuildSheet(mark, sheetViews, schedules, result);

        tx.Commit();
    }

    private List<ViewSchedule> BuildSchedule(string mark, ViewRunResult result)
    {
        var list = new List<ViewSchedule>();
        if (!_cfg.CreateSchedule || string.IsNullOrWhiteSpace(mark)) return list;
        try
        {
            string name = UniqueName(Token(_cfg.ScheduleNameTemplate, mark));
            list.Add(new SlabScheduleBuilder(_doc).BuildRebarSchedule(mark, name));
            result.SchedulesCreated++;
        }
        catch (Exception ex) { result.Error($"Schedule for {mark}: {ex.Message}"); }
        return list;
    }

    private void BuildSheet(string mark, IReadOnlyList<View> views, IReadOnlyList<ViewSchedule> schedules, ViewRunResult result)
    {
        try
        {
            var builder = new SlabSheetBuilder(_doc, _cfg);
            string number = UniqueSheetNumber(Token(_cfg.SheetNumberTemplate, mark));
            string name = Token(_cfg.SheetNameTemplate, mark);
            ViewSheet sheet = builder.CreateSheet(number, name);
            builder.PlaceOnSheet(sheet, views, schedules);
            result.SheetsCreated++;
        }
        catch (Exception ex) { result.Error($"Sheet for {mark}: {ex.Message}"); }
    }

    // ── 3D isolated cage + bending details (like ColumnViews) ─────────────────────

    private View? Create3D(Floor floor, BoundingBoxXYZ bbox, string mark, ViewRunResult result)
    {
        try
        {
            ViewFamilyType vft = FindViewFamilyType(ViewFamily.ThreeDimensional, null);
            View3D v = View3D.CreateIsometric(_doc, vft.Id);

            double p = _cfg.CropPadding;
            v.SetSectionBox(new BoundingBoxXYZ
            {
                Min = new XYZ(bbox.Min.X - p, bbox.Min.Y - p, bbox.Min.Z - p),
                Max = new XYZ(bbox.Max.X + p, bbox.Max.Y + p, bbox.Max.Z + p),
            });
            v.IsSectionBoxActive = true;
            v.Name = UniqueName(Token(_cfg.View3DNameTemplate, mark));
            TrySet(() => v.Scale = _cfg.View3DScale);
            TrySet(() => v.DetailLevel = _cfg.DetailLevel);

            _doc.Regenerate();
            List<Rebar> rebar = HostSrRebar(floor.Id);
            var keep = new HashSet<ElementId>(rebar.Select(r => r.Id));
            List<ElementId> hide = new FilteredElementCollector(_doc, v.Id)
                .WhereElementIsNotElementType()
                .Where(e => !keep.Contains(e.Id) && e.CanBeHidden(v))
                .Select(e => e.Id).ToList();
            if (hide.Count > 0) try { v.HideElements(hide); } catch { }
            foreach (Rebar r in rebar) try { r.SetUnobscuredInView(v, true); } catch { }
            return v;
        }
        catch (Exception ex) { result.Error($"3D cage for {mark}: {ex.Message}"); return null; }
    }

    private View? CreateBendingDetailView(Floor floor, string mark, ViewRunResult result)
    {
        try
        {
            List<Rebar> rebar = HostSrRebar(floor.Id);
            if (rebar.Count == 0) return null;

            ViewFamilyType vft = FindViewFamilyType(ViewFamily.Drafting, null);
            ViewDrafting view = ViewDrafting.Create(_doc, vft.Id);
            view.Name = UniqueName(Token(_cfg.BendingDetailNameTemplate, mark));
            TrySet(() => view.Scale = _cfg.BendingDetailScale);

            RebarBendingDetailType? bdType = new FilteredElementCollector(_doc)
                .OfClass(typeof(RebarBendingDetailType)).Cast<RebarBendingDetailType>().FirstOrDefault();
            if (bdType is null) { result.Error($"Bending details for {mark}: no RebarBendingDetailType in project."); return view; }

            List<Rebar> unique = rebar
                .GroupBy(r => (BarTypeName(r), BarShapeName(r), Math.Round(BarTotalLength(r), 4)))
                .Select(g => g.First()).ToList();

            const double colGap = 5.0, rowGap = 3.0;
            const int cols = 3;
            for (int i = 0; i < unique.Count; i++)
            {
                var pos = new XYZ((i % cols) * colGap, -(i / cols) * rowGap, 0);
                try
                {
                    var bd = RebarBendingDetail.Create(_doc, view.Id, unique[i].Id, 0, bdType, XYZ.Zero, 0);
                    try { RebarBendingDetail.SetPosition(bd, pos); } catch { }
                }
                catch { /* one detail failing must not sink the view */ }
            }
            return view;
        }
        catch (Exception ex) { result.Error($"Bending details for {mark}: {ex.Message}"); return null; }
    }

    // ── Cross-sections through the slab (like ColumnViews' elevations) ────────────

    private List<View> CreateSections(Floor floor, BoundingBoxXYZ bbox, string mark, ViewRunResult result)
    {
        var views = new List<View>();
        foreach ((XYZ look, string dir) in new[] { (XYZ.BasisX, "A-A"), (XYZ.BasisY, "B-B") })
        {
            try
            {
                ViewFamilyType vft = FindViewFamilyType(ViewFamily.Section, _cfg.SectionViewTypeName);
                XYZ basisZ = look.Negate().Normalize();                 // toward the viewer
                XYZ basisY = XYZ.BasisZ;                                // world up
                XYZ basisX = basisY.CrossProduct(basisZ).Normalize();  // section "right"
                XYZ center = (bbox.Min + bbox.Max) / 2.0;

                double pad = _cfg.CropPadding;
                double hLocal = HalfExtentAlongAxis(bbox, basisX);     // half section width
                double vLocal = (bbox.Max.Z - bbox.Min.Z) / 2.0;       // half slab thickness
                double dLocal = HalfExtentAlongAxis(bbox, basisZ);     // half look-depth (whole slab)

                var sectionBox = new BoundingBoxXYZ
                {
                    Transform = new Transform(Transform.Identity)
                    { Origin = center, BasisX = basisX, BasisY = basisY, BasisZ = basisZ },
                    Min = new XYZ(-hLocal - pad, -vLocal - pad, -(dLocal + _cfg.SectionDepthFt)),
                    Max = new XYZ( hLocal + pad,  vLocal + pad,  (dLocal + _cfg.SectionDepthFt)),
                };

                ViewSection view = ViewSection.CreateSection(_doc, vft.Id, sectionBox);
                view.CropBoxActive = true;
                view.CropBoxVisible = false;
                view.Name = UniqueName(_cfg.SectionNameTemplate.Replace("{Mark}", mark).Replace("{Dir}", dir));
                ApplyAppearance(view);
                ApplyTemplate(view);

                _doc.Regenerate();
                foreach (Rebar r in HostSrRebar(floor.Id))
                    try { r.SetUnobscuredInView(view, true); } catch { /* best-effort */ }

                views.Add(view);
                result.ViewsCreated++;
            }
            catch (Exception ex) { result.Error($"Section {dir} for {mark}: {ex.Message}"); }
        }
        return views;
    }

    private static double HalfExtentAlongAxis(BoundingBoxXYZ bbox, XYZ axis)
    {
        XYZ d = bbox.Max - bbox.Min;
        return (Math.Abs(d.X * axis.X) + Math.Abs(d.Y * axis.Y) + Math.Abs(d.Z * axis.Z)) / 2.0;
    }

    private List<Rebar> HostSrRebar(ElementId floorId) =>
        new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Rebar)
            .WhereElementIsNotElementType().OfType<Rebar>()
            .Where(r => r.GetHostId() == floorId)
            .Where(r => r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()?.StartsWith("SR:") == true)
            .ToList();

    private string BarTypeName(Rebar r) => _doc.GetElement(r.GetTypeId())?.Name ?? "";
    private static string BarShapeName(Rebar r) => r.LookupParameter("Shape")?.AsValueString() ?? "";
    private static double BarTotalLength(Rebar r) =>
        r.LookupParameter("Total Bar Length")?.AsDouble() ?? r.LookupParameter("Bar Length")?.AsDouble() ?? 0.0;

    private static string Token(string template, string mark) => template.Replace("{Mark}", mark);

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

    private void CropAndRange(ViewPlan plan, BoundingBoxXYZ bbox, Level level)
    {
        double pad = _cfg.CropPadding;
        BoundingBoxXYZ existing = plan.CropBox;
        plan.CropBoxActive = true;
        plan.CropBoxVisible = false;
        plan.CropBox = new BoundingBoxXYZ
        {
            Min = new XYZ(bbox.Min.X - pad, bbox.Min.Y - pad, existing.Min.Z),
            Max = new XYZ(bbox.Max.X + pad, bbox.Max.Y + pad, existing.Max.Z),
        };

        double lvl = level.ProjectElevation;
        // Cut plane ABOVE the slab so the whole mat projects into the downward plan; each
        // layer's bars are then drawn via SetUnobscuredInView (rebar that is only "beyond" the
        // cut plane is not rendered otherwise). Verified in-Revit on a real slab.
        double top = bbox.Max.Z - lvl + 1.5;
        double cut = bbox.Max.Z - lvl + 1.0;
        double bottom = bbox.Min.Z - lvl - 0.5;

        PlanViewRange vr = plan.GetViewRange();
        vr.SetLevelId(PlanViewPlane.TopClipPlane, level.Id);
        vr.SetOffset(PlanViewPlane.TopClipPlane, top);
        vr.SetLevelId(PlanViewPlane.CutPlane, level.Id);
        vr.SetOffset(PlanViewPlane.CutPlane, cut);
        vr.SetLevelId(PlanViewPlane.BottomClipPlane, level.Id);
        vr.SetOffset(PlanViewPlane.BottomClipPlane, bottom);
        vr.SetLevelId(PlanViewPlane.ViewDepthPlane, level.Id);
        vr.SetOffset(PlanViewPlane.ViewDepthPlane, bottom);
        plan.SetViewRange(vr);
    }

    private void IsolateLayer(View view, ElementId slabId, SlabLayer layer)
    {
        string suffix = $":{slabId.Value}:{layer}";

        // This layer's bars: show as unobscured solid lines. Without this, horizontal slab bars
        // (seen in projection, not cut) are not drawn in a plan view at all — verified in-Revit.
        foreach (Element e in new FilteredElementCollector(_doc, view.Id)
                     .OfCategory(BuiltInCategory.OST_Rebar).WhereElementIsNotElementType().ToList())
            if (TagMatches(e, suffix) && e is Autodesk.Revit.DB.Structure.Rebar bar)
                try { bar.SetUnobscuredInView(view, true); } catch { /* best-effort */ }

        if (_cfg.Isolation == LayerIsolation.Show) return;

        List<Element> others = new FilteredElementCollector(_doc, view.Id)
            .OfCategory(BuiltInCategory.OST_Rebar).WhereElementIsNotElementType()
            .Where(e => !TagMatches(e, suffix))
            .ToList();
        if (others.Count == 0) return;

        if (_cfg.Isolation == LayerIsolation.Halftone)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetHalftone(true);
            foreach (Element e in others)
                try { view.SetElementOverrides(e.Id, ogs); } catch { /* best-effort */ }
        }
        else
        {
            List<ElementId> ids = others.Where(e => e.CanBeHidden(view)).Select(e => e.Id).ToList();
            if (ids.Count > 0)
                try { view.HideElements(ids); } catch { /* best-effort */ }
        }
    }

    private static bool TagMatches(Element e, string suffix)
    {
        string? tag = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
        return tag is not null
            && tag.StartsWith("SR:", StringComparison.Ordinal)
            && tag.EndsWith(suffix, StringComparison.Ordinal);
    }

    private void NameView(View view, string mark, int num, string label)
    {
        string name = _cfg.LayerViewNameTemplate
            .Replace("{Mark}", mark)
            .Replace("{N}", num.ToString())
            .Replace("{Layer}", label)
            .Replace("{Level}", view.GenLevel?.Name ?? "")
            .Trim();
        if (string.IsNullOrEmpty(name)) name = $"View {view.Id.Value}";
        view.Name = UniqueName(name);
    }

    private void ApplyAppearance(View view)
    {
        TrySet(() => view.Scale = _cfg.PlanScale);
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

    private static void TrySet(Action set)
    {
        try { set(); }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
    }

    private string MarkOf(Element e) =>
        e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
}
