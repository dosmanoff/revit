using System.Globalization;
using System.Text.Json;
using Autodesk.Revit.DB;
using RevitActionRecorder.Configuration;

namespace RevitActionRecorder.Snapshot;

/// <summary>Оформительский слой снапшота: виды, переопределения, стили объектов, ресурсы, листы, спецификации, предупреждения.</summary>
internal static class DressingWriter
{
    private static readonly ViewType[] SkippedViewTypes =
    [
        ViewType.Undefined, ViewType.Internal, ViewType.ProjectBrowser, ViewType.SystemBrowser,
    ];

    // ---- виды -------------------------------------------------------------

    public static long WriteViews(Utf8JsonWriter w, Document doc, RecorderConfig config, ProgressWindow progress)
    {
        var allCategories = CollectCategories(doc);
        long count = 0;

        var views = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !SkippedViewTypes.Contains(v.ViewType))
            .ToList();

        w.WritePropertyName("views");
        w.WriteStartArray();
        long done = 0;
        foreach (var view in views)
        {
            WriteView(w, doc, view, allCategories, config);
            count++;
            done++;
            // Тик на каждом виде: с включёнными поэлементными переопределениями один вид
            // может считаться секундами, и более редкая проверка делала «Отмену» неотзывчивой.
            progress.Report($"Виды: {done} / {views.Count}", (double)done / Math.Max(1, views.Count));
            if (progress.CancelRequested) throw new OperationCanceledException();
        }
        w.WriteEndArray();
        return count;
    }

    private static void WriteView(Utf8JsonWriter w, Document doc, View view, List<Category> allCategories, RecorderConfig config)
    {
        w.WriteStartObject();
        try
        {
            w.WriteNumber("id", view.Id.Value);
            w.WriteString("uid", view.UniqueId);
            try { w.WriteString("name", view.Name); } catch { }
            w.WriteString("viewType", view.ViewType.ToString());
            if (view.IsTemplate) w.WriteBoolean("isTemplate", true);

            try
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                    w.WriteNumber("templateId", view.ViewTemplateId.Value);
            }
            catch { }

            try { w.WriteNumber("scale", view.Scale); } catch { }
            try { w.WriteString("detailLevel", view.DetailLevel.ToString()); } catch { }
            try { w.WriteString("displayStyle", view.DisplayStyle.ToString()); } catch { }

            try
            {
                w.WriteBoolean("cropActive", view.CropBoxActive);
                w.WriteBoolean("cropVisible", view.CropBoxVisible);
                var crop = view.CropBox;
                if (crop is not null)
                {
                    w.WritePropertyName("cropBox");
                    w.WriteStartObject();
                    SnapshotWriter.WriteXyz(w, "min", crop.Min);
                    SnapshotWriter.WriteXyz(w, "max", crop.Max);
                    w.WriteEndObject();
                }
            }
            catch { }

            try
            {
                if (view is ViewPlan plan)
                {
                    var range = plan.GetViewRange();
                    w.WritePropertyName("viewRange");
                    w.WriteStartObject();
                    foreach (var plane in new[]
                             {
                                 PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane,
                                 PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane,
                                 PlanViewPlane.UnderlayBottom,
                             })
                    {
                        w.WritePropertyName(plane.ToString());
                        w.WriteStartObject();
                        try { w.WriteNumber("levelId", range.GetLevelId(plane).Value); } catch { }
                        try { w.WriteNumber("offset", range.GetOffset(plane)); } catch { }
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
            }
            catch { }

            try
            {
                if (view is View3D view3d)
                {
                    var orientation = view3d.GetOrientation();
                    w.WritePropertyName("orientation3d");
                    w.WriteStartObject();
                    SnapshotWriter.WriteXyz(w, "eye", orientation.EyePosition);
                    SnapshotWriter.WriteXyz(w, "forward", orientation.ForwardDirection);
                    SnapshotWriter.WriteXyz(w, "up", orientation.UpDirection);
                    w.WriteEndObject();
                    if (view3d.IsPerspective) w.WriteBoolean("perspective", true);
                }
            }
            catch { }

            try
            {
                var nonControlled = view.GetNonControlledTemplateParameterIds();
                if (nonControlled.Count > 0)
                {
                    w.WritePropertyName("nonControlledTemplateParams");
                    w.WriteStartArray();
                    foreach (var id in nonControlled) w.WriteNumberValue(id.Value);
                    w.WriteEndArray();
                }
            }
            catch { }

            WriteCategoryOverrides(w, view, allCategories);
            // Листы и спецификации не содержат переопределяемой модели — сканировать их бессмысленно.
            if (config.CollectElementOverrides && !view.IsTemplate
                && view is not ViewSheet && view is not ViewSchedule)
                WriteElementOverrides(w, doc, view);
            WriteFilters(w, doc, view);
        }
        catch
        {
        }
        w.WriteEndObject();
    }

    private static void WriteCategoryOverrides(Utf8JsonWriter w, View view, List<Category> allCategories)
    {
        bool started = false;
        foreach (var category in allCategories)
        {
            bool hidden = false;
            OverrideGraphicSettings? ogs = null;
            try { hidden = view.GetCategoryHidden(category.Id); } catch { }
            try
            {
                var candidate = view.GetCategoryOverrides(category.Id);
                if (candidate is not null && !OgsWriter.IsDefault(candidate)) ogs = candidate;
            }
            catch { }

            if (!hidden && ogs is null)
                continue;

            if (!started)
            {
                w.WritePropertyName("categoryOverrides");
                w.WriteStartArray();
                started = true;
            }

            w.WriteStartObject();
            w.WriteNumber("catId", category.Id.Value);
            try { w.WriteString("cat", category.Name); } catch { }
            if (hidden) w.WriteBoolean("hidden", true);
            if (ogs is not null) OgsWriter.Write(w, "ogs", ogs);
            w.WriteEndObject();
        }
        if (started) w.WriteEndArray();
    }

    private static void WriteElementOverrides(Utf8JsonWriter w, Document doc, View view)
    {
        try
        {
            bool started = false;
            foreach (var element in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                OverrideGraphicSettings? ogs = null;
                try
                {
                    var candidate = view.GetElementOverrides(element.Id);
                    if (candidate is not null && !OgsWriter.IsDefault(candidate)) ogs = candidate;
                }
                catch { }
                if (ogs is null) continue;

                if (!started)
                {
                    w.WritePropertyName("elementOverrides");
                    w.WriteStartArray();
                    started = true;
                }
                w.WriteStartObject();
                w.WriteNumber("id", element.Id.Value);
                OgsWriter.Write(w, "ogs", ogs);
                w.WriteEndObject();
            }
            if (started) w.WriteEndArray();
        }
        catch
        {
            // не каждый вид поддерживает коллектор (спецификации и т.п.)
        }
    }

    private static void WriteFilters(Utf8JsonWriter w, Document doc, View view)
    {
        try
        {
            var filters = view.GetFilters();
            if (filters.Count == 0) return;

            w.WritePropertyName("filters");
            w.WriteStartArray();
            foreach (var filterId in filters)
            {
                w.WriteStartObject();
                w.WriteNumber("id", filterId.Value);
                try { w.WriteString("name", doc.GetElement(filterId)?.Name); } catch { }
                try { w.WriteBoolean("visible", view.GetFilterVisibility(filterId)); } catch { }
                try { w.WriteBoolean("enabled", view.GetIsFilterEnabled(filterId)); } catch { }
                try
                {
                    var ogs = view.GetFilterOverrides(filterId);
                    if (ogs is not null && !OgsWriter.IsDefault(ogs)) OgsWriter.Write(w, "ogs", ogs);
                }
                catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        catch
        {
        }
    }

    // ---- стили объектов ---------------------------------------------------

    public static void WriteObjectStyles(Utf8JsonWriter w, Document doc)
    {
        w.WritePropertyName("categories");
        w.WriteStartArray();
        foreach (var category in CollectCategories(doc))
        {
            w.WriteStartObject();
            try
            {
                w.WriteNumber("id", category.Id.Value);
                w.WriteString("name", category.Name);
                try
                {
                    var parent = category.Parent;
                    if (parent is not null) w.WriteNumber("parent", parent.Id.Value);
                }
                catch { }
                try
                {
                    var weight = category.GetLineWeight(GraphicsStyleType.Projection);
                    if (weight.HasValue) w.WriteNumber("lineWeightProj", weight.Value);
                }
                catch { }
                try
                {
                    var weight = category.GetLineWeight(GraphicsStyleType.Cut);
                    if (weight.HasValue) w.WriteNumber("lineWeightCut", weight.Value);
                }
                catch { }
                try
                {
                    var color = category.LineColor;
                    if (color is not null && color.IsValid)
                    {
                        w.WritePropertyName("color");
                        w.WriteStartArray();
                        w.WriteNumberValue(color.Red);
                        w.WriteNumberValue(color.Green);
                        w.WriteNumberValue(color.Blue);
                        w.WriteEndArray();
                    }
                }
                catch { }
                try
                {
                    var pattern = category.GetLinePatternId(GraphicsStyleType.Projection);
                    if (pattern != ElementId.InvalidElementId) w.WriteNumber("linePatternProj", pattern.Value);
                }
                catch { }
                try
                {
                    var pattern = category.GetLinePatternId(GraphicsStyleType.Cut);
                    if (pattern != ElementId.InvalidElementId) w.WriteNumber("linePatternCut", pattern.Value);
                }
                catch { }
                try
                {
                    var material = category.Material;
                    if (material is not null) w.WriteNumber("material", material.Id.Value);
                }
                catch { }
            }
            catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static List<Category> CollectCategories(Document doc)
    {
        var result = new List<Category>();
        try
        {
            foreach (Category category in doc.Settings.Categories)
            {
                result.Add(category);
                try
                {
                    foreach (Category sub in category.SubCategories)
                        result.Add(sub);
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    // ---- ресурсы оформления ----------------------------------------------

    public static void WriteResources(Utf8JsonWriter w, Document doc)
    {
        w.WritePropertyName("resources");
        w.WriteStartObject();

        w.WritePropertyName("linePatterns");
        w.WriteStartArray();
        foreach (var lp in new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)))
        {
            w.WriteStartObject();
            w.WriteNumber("id", lp.Id.Value);
            try { w.WriteString("name", lp.Name); } catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WritePropertyName("fillPatterns");
        w.WriteStartArray();
        foreach (var fpe in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
        {
            w.WriteStartObject();
            w.WriteNumber("id", fpe.Id.Value);
            try { w.WriteString("name", fpe.Name); } catch { }
            try
            {
                var fp = fpe.GetFillPattern();
                w.WriteString("target", fp.Target.ToString());
                if (fp.IsSolidFill) w.WriteBoolean("solid", true);
            }
            catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WritePropertyName("materials");
        w.WriteStartArray();
        foreach (var material in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
        {
            w.WriteStartObject();
            w.WriteNumber("id", material.Id.Value);
            try { w.WriteString("name", material.Name); } catch { }
            try
            {
                var color = material.Color;
                if (color is not null && color.IsValid)
                {
                    w.WritePropertyName("color");
                    w.WriteStartArray();
                    w.WriteNumberValue(color.Red);
                    w.WriteNumberValue(color.Green);
                    w.WriteNumberValue(color.Blue);
                    w.WriteEndArray();
                }
                w.WriteNumber("transparency", material.Transparency);
                if (material.SurfaceForegroundPatternId != ElementId.InvalidElementId)
                    w.WriteNumber("surfForePattern", material.SurfaceForegroundPatternId.Value);
                if (material.SurfaceBackgroundPatternId != ElementId.InvalidElementId)
                    w.WriteNumber("surfBackPattern", material.SurfaceBackgroundPatternId.Value);
                if (material.CutForegroundPatternId != ElementId.InvalidElementId)
                    w.WriteNumber("cutForePattern", material.CutForegroundPatternId.Value);
                if (material.CutBackgroundPatternId != ElementId.InvalidElementId)
                    w.WriteNumber("cutBackPattern", material.CutBackgroundPatternId.Value);
                if (material.AppearanceAssetId != ElementId.InvalidElementId)
                    w.WriteNumber("appearanceAsset", material.AppearanceAssetId.Value);
            }
            catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WritePropertyName("appearanceAssets");
        w.WriteStartArray();
        foreach (var asset in new FilteredElementCollector(doc).OfClass(typeof(AppearanceAssetElement)))
        {
            w.WriteStartObject();
            w.WriteNumber("id", asset.Id.Value);
            try { w.WriteString("name", asset.Name); } catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // TextNoteType, DimensionType, типы марок и линий — это ElementType'ы:
        // они уже полностью выгружены в разделе types со всеми параметрами.
        w.WriteEndObject();
    }

    // ---- листы ------------------------------------------------------------

    public static void WriteSheets(Utf8JsonWriter w, Document doc)
    {
        w.WritePropertyName("sheets");
        w.WriteStartArray();
        foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
        {
            w.WriteStartObject();
            try
            {
                w.WriteNumber("id", sheet.Id.Value);
                w.WriteString("uid", sheet.UniqueId);
                w.WriteString("number", sheet.SheetNumber);
                try { w.WriteString("name", sheet.Name); } catch { }

                try
                {
                    var titleBlock = new FilteredElementCollector(doc, sheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsNotElementType()
                        .FirstElement();
                    if (titleBlock is not null)
                    {
                        w.WriteNumber("titleBlockType", titleBlock.GetTypeId().Value);
                        if (doc.GetElement(titleBlock.GetTypeId()) is ElementType tbt)
                            w.WriteString("titleBlock", $"{tbt.FamilyName}: {tbt.Name}");
                    }
                }
                catch { }

                try
                {
                    var revisions = sheet.GetAllRevisionIds();
                    if (revisions.Count > 0)
                    {
                        w.WritePropertyName("revisions");
                        w.WriteStartArray();
                        foreach (var id in revisions) w.WriteNumberValue(id.Value);
                        w.WriteEndArray();
                    }
                    var additional = sheet.GetAdditionalRevisionIds();
                    if (additional.Count > 0)
                    {
                        w.WritePropertyName("additionalRevisions");
                        w.WriteStartArray();
                        foreach (var id in additional) w.WriteNumberValue(id.Value);
                        w.WriteEndArray();
                    }
                }
                catch { }

                w.WritePropertyName("viewports");
                w.WriteStartArray();
                foreach (var vp in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
                {
                    w.WriteStartObject();
                    try
                    {
                        w.WriteNumber("id", vp.Id.Value);
                        w.WriteNumber("viewId", vp.ViewId.Value);
                        w.WriteNumber("typeId", vp.GetTypeId().Value);
                        SnapshotWriter.WriteXyz(w, "center", vp.GetBoxCenter());
                        try
                        {
                            var outline = vp.GetBoxOutline();
                            w.WritePropertyName("outline");
                            w.WriteStartObject();
                            SnapshotWriter.WriteXyz(w, "min", outline.MinimumPoint);
                            SnapshotWriter.WriteXyz(w, "max", outline.MaximumPoint);
                            w.WriteEndObject();
                        }
                        catch { }
                        try { SnapshotWriter.WriteXyz(w, "labelOffset", vp.LabelOffset); } catch { }
                    }
                    catch { }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    // ---- спецификации -----------------------------------------------------

    public static void WriteSchedules(Utf8JsonWriter w, Document doc)
    {
        w.WritePropertyName("schedules");
        w.WriteStartArray();
        foreach (var schedule in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
        {
            w.WriteStartObject();
            try
            {
                w.WriteNumber("id", schedule.Id.Value);
                w.WriteString("uid", schedule.UniqueId);
                try { w.WriteString("name", schedule.Name); } catch { }
                if (schedule.IsTemplate) w.WriteBoolean("isTemplate", true);

                var definition = schedule.Definition;
                try { w.WriteBoolean("itemized", definition.IsItemized); } catch { }
                try { w.WriteBoolean("grandTotal", definition.ShowGrandTotal); } catch { }

                w.WritePropertyName("fields");
                w.WriteStartArray();
                for (int i = 0; i < definition.GetFieldCount(); i++)
                {
                    w.WriteStartObject();
                    try
                    {
                        var field = definition.GetField(i);
                        w.WriteNumber("index", i);
                        try { w.WriteString("heading", field.ColumnHeading); } catch { }
                        try { w.WriteString("fieldType", field.FieldType.ToString()); } catch { }
                        try { w.WriteNumber("paramId", field.ParameterId.Value); } catch { }
                        try { if (field.IsHidden) w.WriteBoolean("hidden", true); } catch { }
                    }
                    catch { }
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                try
                {
                    var filters = definition.GetFilters();
                    if (filters.Count > 0)
                    {
                        w.WritePropertyName("filters");
                        w.WriteStartArray();
                        foreach (var filter in filters)
                        {
                            w.WriteStartObject();
                            try
                            {
                                w.WriteString("filterType", filter.FilterType.ToString());
                                try
                                {
                                    var fieldIndex = definition.GetFieldIndex(filter.FieldId);
                                    w.WriteNumber("fieldIndex", fieldIndex);
                                }
                                catch { }
                                try
                                {
                                    if (filter.IsStringValue) w.WriteString("value", filter.GetStringValue());
                                    else if (filter.IsDoubleValue) w.WriteNumber("value", filter.GetDoubleValue());
                                    else if (filter.IsIntegerValue) w.WriteNumber("value", filter.GetIntegerValue());
                                    else if (filter.IsElementIdValue) w.WriteNumber("value", filter.GetElementIdValue().Value);
                                }
                                catch { }
                            }
                            catch { }
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                    }
                }
                catch { }

                try
                {
                    var sortGroup = definition.GetSortGroupFields();
                    if (sortGroup.Count > 0)
                    {
                        w.WritePropertyName("sortGroup");
                        w.WriteStartArray();
                        foreach (var sg in sortGroup)
                        {
                            w.WriteStartObject();
                            try
                            {
                                try
                                {
                                    var fieldIndex = definition.GetFieldIndex(sg.FieldId);
                                    w.WriteNumber("fieldIndex", fieldIndex);
                                }
                                catch { }
                                w.WriteString("order", sg.SortOrder.ToString());
                                if (sg.ShowHeader) w.WriteBoolean("showHeader", true);
                                if (sg.ShowFooter) w.WriteBoolean("showFooter", true);
                                if (sg.ShowBlankLine) w.WriteBoolean("showBlankLine", true);
                            }
                            catch { }
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                    }
                }
                catch { }
            }
            catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    // ---- легенды и предупреждения -----------------------------------------

    public static void WriteLegends(Utf8JsonWriter w, Document doc)
    {
        w.WritePropertyName("legends");
        w.WriteStartArray();
        foreach (var view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
        {
            if (view.ViewType != ViewType.Legend || view.IsTemplate) continue;
            w.WriteStartObject();
            w.WriteNumber("id", view.Id.Value);
            try { w.WriteString("name", view.Name); } catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    public static void WriteWarnings(Utf8JsonWriter w, Document doc)
    {
        w.WritePropertyName("warnings");
        w.WriteStartArray();
        foreach (var warning in doc.GetWarnings())
        {
            w.WriteStartObject();
            try
            {
                try { w.WriteString("severity", warning.GetSeverity().ToString()); } catch { }
                try { w.WriteString("description", warning.GetDescriptionText()); } catch { }
                try { w.WriteString("defId", warning.GetFailureDefinitionId().Guid.ToString()); } catch { }
                try
                {
                    w.WritePropertyName("elements");
                    w.WriteStartArray();
                    foreach (var id in warning.GetFailingElements()) w.WriteNumberValue(id.Value);
                    w.WriteEndArray();
                }
                catch { }
                try
                {
                    var additional = warning.GetAdditionalElements();
                    if (additional.Count > 0)
                    {
                        w.WritePropertyName("additionalElements");
                        w.WriteStartArray();
                        foreach (var id in additional) w.WriteNumberValue(id.Value);
                        w.WriteEndArray();
                    }
                }
                catch { }
            }
            catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
}
