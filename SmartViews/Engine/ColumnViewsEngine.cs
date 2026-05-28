using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SmartViews.Config;
using View = Autodesk.Revit.DB.View;

namespace SmartViews.Engine;

/// <summary>
/// Generates a documentation set for each selected structural column:
/// two perpendicular elevations (shallow depth), two end plans (top/bottom),
/// with rebar hosted by other columns hidden or half-toned. Must run inside an
/// open TransactionGroup.
/// </summary>
public sealed class ColumnViewsEngine
{
    private readonly Document _doc;
    private readonly ColumnViewsConfig _cfg;
    private readonly HashSet<string> _existingViewNames;
    private readonly HashSet<string> _existingSheetNumbers;

    public ColumnViewsEngine(Document doc, ColumnViewsConfig cfg)
    {
        _doc = doc;
        _cfg = cfg;
        _existingViewNames = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _existingSheetNumbers = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(s => s.SheetNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public ViewCreationResult Run(IList<ElementId> columnIds)
    {
        var result = new ViewCreationResult();

        foreach (ElementId id in columnIds)
        {
            if (_doc.GetElement(id) is not FamilyInstance fi)
            {
                result.RecordError($"Element {id.Value} is not a column — skipped.");
                continue;
            }

            try
            {
                ProcessColumn(fi, result);
            }
            catch (Exception ex)
            {
                result.RecordError($"Column {id.Value} ({MarkOf(fi)}): {ex.Message}");
            }
        }

        return result;
    }

    // -----------------------------------------------------------------------

    private void ProcessColumn(FamilyInstance fi, ViewCreationResult result)
    {
        BoundingBoxXYZ? bbox = fi.get_BoundingBox(null);
        if (bbox is null)
        {
            result.RecordError($"Column {fi.Id.Value} has no bounding box — skipped.");
            return;
        }

        string mark = MarkOf(fi);
        (XYZ fwd, XYZ rgt) = Orientation(fi);

        using var tx = new Transaction(_doc, $"Column Views — {mark}");
        tx.Start();

        var created = new List<View>();

        // Two perpendicular elevations (look along the column's two in-plan axes).
        View? front = CreateElevation(bbox, lookDir: fwd, mark, "Front");
        if (front is not null) created.Add(front);

        View? side = CreateElevation(bbox, lookDir: rgt, mark, "Side");
        if (side is not null) created.Add(side);

        // Two end plans cut near the top and bottom faces of the column.
        View? topPlan = CreatePlan(fi, bbox, mark, isTop: true);
        if (topPlan is not null) created.Add(topPlan);

        View? botPlan = CreatePlan(fi, bbox, mark, isTop: false);
        if (botPlan is not null) created.Add(botPlan);

        // Visibility computed from the new crops before we query what's visible.
        _doc.Regenerate();

        foreach (View v in created)
        {
            ApplyForeignRebarTreatment(v, mark, result);
            result.RecordCreated();
        }

        List<ViewSchedule> schedules = BuildSchedules(mark, result);

        if (_cfg.PlaceOnSheet)
            PlaceOnSheet(fi, mark, created, schedules, result);

        tx.Commit();
    }

    private List<ViewSchedule> BuildSchedules(string mark, ViewCreationResult result)
    {
        var schedules = new List<ViewSchedule>();
        if (string.IsNullOrWhiteSpace(mark))
            return schedules;

        var builder = new ColumnScheduleBuilder(_doc);

        if (_cfg.CreateRebarSchedule)
        {
            try
            {
                ViewSchedule? s = builder.BuildRebarSchedule(
                    mark, UniqueName(Token(_cfg.RebarScheduleNameTemplate, mark)));
                if (s is not null) { schedules.Add(s); result.RecordCreated(); }
            }
            catch (Exception ex)
            {
                result.RecordError($"Rebar schedule for {mark}: {ex.Message}");
            }
        }

        if (_cfg.CreateBendingSchedule)
        {
            try
            {
                ViewSchedule? s = builder.BuildBendingSchedule(
                    mark, UniqueName(Token(_cfg.BendingScheduleNameTemplate, mark)));
                if (s is not null) { schedules.Add(s); result.RecordCreated(); }
            }
            catch (Exception ex)
            {
                result.RecordError($"Bending schedule for {mark}: {ex.Message}");
            }
        }

        return schedules;
    }

    private void PlaceOnSheet(
        FamilyInstance fi,
        string mark,
        IReadOnlyList<View> views,
        IReadOnlyList<ViewSchedule> schedules,
        ViewCreationResult result)
    {
        try
        {
            string levelName = ResolveLevel(fi)?.Name ?? string.Empty;
            string number = UniqueSheetNumber(
                Token(_cfg.SheetNumberTemplate, mark).Replace("{Level}", levelName));
            string name = Token(_cfg.SheetNameTemplate, mark).Replace("{Level}", levelName);

            var sheetBuilder = new ColumnSheetBuilder(_doc, _cfg);
            ViewSheet sheet = sheetBuilder.CreateSheet(number, name);
            sheetBuilder.PlaceOnSheet(sheet, views, schedules);
        }
        catch (Exception ex)
        {
            result.RecordError($"Sheet for {mark}: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Elevations
    // -----------------------------------------------------------------------

    private View? CreateElevation(BoundingBoxXYZ bbox, XYZ lookDir, string mark, string direction)
    {
        ViewFamilyType vft = FindViewFamilyType(ViewFamily.Section, _cfg.SectionViewTypeName);

        XYZ basisZ = lookDir.Negate().Normalize();            // points toward the viewer
        XYZ basisY = XYZ.BasisZ;                              // world up
        XYZ basisX = basisY.CrossProduct(basisZ).Normalize(); // right, right-handed frame

        double half = _cfg.CropPadding;
        double hLocal = HalfExtentAlongAxis(bbox, basisX);
        double vLocal = (bbox.Max.Z - bbox.Min.Z) / 2.0;
        double dLocal = HalfExtentAlongAxis(bbox, basisZ);

        XYZ center = (bbox.Min + bbox.Max) / 2.0;

        var sectionBox = new BoundingBoxXYZ
        {
            Transform = new Transform(Transform.Identity)
            {
                Origin = center,
                BasisX = basisX,
                BasisY = basisY,
                BasisZ = basisZ,
            },
            Min = new XYZ(-hLocal - half, -vLocal - half, -(dLocal + _cfg.ElevationDepth)),
            Max = new XYZ( hLocal + half,  vLocal + half,  (dLocal + _cfg.ElevationDepth)),
        };

        ViewSection view = ViewSection.CreateSection(_doc, vft.Id, sectionBox);

        Name(view, _cfg.ElevationNameTemplate, mark, direction: direction, end: null);
        ApplyTemplate(view, _cfg.ElevationViewTemplate);
        return view;
    }

    // -----------------------------------------------------------------------
    // End plans
    // -----------------------------------------------------------------------

    private View? CreatePlan(FamilyInstance fi, BoundingBoxXYZ bbox, string mark, bool isTop)
    {
        Level? level = ResolveLevel(fi);
        if (level is null)
            throw new InvalidOperationException("cannot determine a base Level for the column.");

        ViewFamilyType vft = FindViewFamilyType(ViewFamily.FloorPlan, _cfg.PlanViewTypeName);
        ViewPlan plan = ViewPlan.Create(_doc, vft.Id, level.Id);

        double pad = _cfg.CropPadding;
        BoundingBoxXYZ existing = plan.CropBox;
        plan.CropBoxActive = true;
        plan.CropBoxVisible = true;
        plan.CropBox = new BoundingBoxXYZ
        {
            Min = new XYZ(bbox.Min.X - pad, bbox.Min.Y - pad, existing.Min.Z),
            Max = new XYZ(bbox.Max.X + pad, bbox.Max.Y + pad, existing.Max.Z),
        };

        // Cut plane near the relevant face, expressed as an offset from the level.
        double levelElev = level.ProjectElevation;
        double cut = isTop
            ? (bbox.Max.Z - levelElev) - _cfg.PlanCutInset
            : (bbox.Min.Z - levelElev) + _cfg.PlanCutInset;

        double top = cut + 0.1;
        double bottom = cut - _cfg.PlanViewDepth;

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

        Name(plan, _cfg.PlanNameTemplate, mark, direction: null, end: isTop ? "Top" : "Bottom");
        ApplyTemplate(plan, _cfg.PlanViewTemplate);
        return plan;
    }

    private Level? ResolveLevel(FamilyInstance fi)
    {
        Parameter? baseLevel = fi.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
        if (baseLevel?.AsElementId() is { } id && id != ElementId.InvalidElementId)
            return _doc.GetElement(id) as Level;

        if (fi.LevelId != ElementId.InvalidElementId)
            return _doc.GetElement(fi.LevelId) as Level;

        return null;
    }

    // -----------------------------------------------------------------------
    // Foreign rebar treatment (hide / halftone bars hosted by other columns)
    // -----------------------------------------------------------------------

    private void ApplyForeignRebarTreatment(View view, string targetMark, ViewCreationResult result)
    {
        if (_cfg.ForeignRebar == ForeignRebarMode.Show)
            return;

        if (string.IsNullOrWhiteSpace(targetMark))
        {
            result.RecordError(
                $"View \"{view.Name}\": column has no Mark, so foreign rebar was left visible.");
            return;
        }

        List<ElementId> foreign = new FilteredElementCollector(_doc, view.Id)
            .OfCategory(BuiltInCategory.OST_Rebar)
            .WhereElementIsNotElementType()
            .Where(e => IsForeignRebar(e, targetMark))
            .Select(e => e.Id)
            .ToList();

        if (foreign.Count == 0)
            return;

        if (_cfg.ForeignRebar == ForeignRebarMode.Halftone)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetHalftone(true);
            foreach (ElementId id in foreign)
                view.SetElementOverrides(id, ogs);
        }
        else // Hide
        {
            try
            {
                view.HideElements(foreign);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // One or more elements refused hiding — hide what we can, one at a time.
                foreach (ElementId id in foreign)
                {
                    try { view.HideElements(new List<ElementId> { id }); }
                    catch (Autodesk.Revit.Exceptions.ArgumentException) { /* skip */ }
                }
            }
        }
    }

    /// <summary>
    /// True when the element is a rebar whose host column Mark is known and differs
    /// from <paramref name="targetMark"/>. Unknown hosts are treated as not-foreign so
    /// the target column's own bars are never hidden by mistake.
    /// </summary>
    private bool IsForeignRebar(Element e, string targetMark)
    {
        if (e is not Rebar rebar)
            return false;

        ElementId hostId = rebar.GetHostId();
        if (hostId == ElementId.InvalidElementId)
            return false;

        string? hostMark = _doc.GetElement(hostId)?
            .get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();

        if (string.IsNullOrWhiteSpace(hostMark))
            return false;

        return !string.Equals(hostMark, targetMark, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (XYZ Forward, XYZ Right) Orientation(FamilyInstance fi)
    {
        var flat = new XYZ(fi.FacingOrientation.X, fi.FacingOrientation.Y, 0);
        XYZ fwd = flat.GetLength() > 1e-6 ? flat.Normalize() : XYZ.BasisY;
        var rgt = new XYZ(fwd.Y, -fwd.X, 0); // 90° CW in XY
        return (fwd, rgt);
    }

    private static double HalfExtentAlongAxis(BoundingBoxXYZ bbox, XYZ axis)
    {
        double halfX = (bbox.Max.X - bbox.Min.X) / 2.0;
        double halfY = (bbox.Max.Y - bbox.Min.Y) / 2.0;
        double halfZ = (bbox.Max.Z - bbox.Min.Z) / 2.0;
        return Math.Abs(axis.X) * halfX
             + Math.Abs(axis.Y) * halfY
             + Math.Abs(axis.Z) * halfZ;
    }

    private string MarkOf(Element e) =>
        e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;

    private void Name(View view, string template, string mark, string? direction, string? end)
    {
        string levelName = view.GenLevel?.Name
            ?? (view.get_Parameter(BuiltInParameter.PLAN_VIEW_LEVEL)?.AsString() ?? string.Empty);

        string name = template
            .Replace("{Mark}", mark)
            .Replace("{Direction}", direction ?? string.Empty)
            .Replace("{End}", end ?? string.Empty)
            .Replace("{Level}", levelName)
            .Trim();

        if (string.IsNullOrEmpty(name))
            name = $"View {view.Id.Value}";

        view.Name = UniqueName(name);
    }

    private string UniqueName(string desired)
    {
        if (!_existingViewNames.Contains(desired))
        {
            _existingViewNames.Add(desired);
            return desired;
        }

        if (_cfg.DuplicateHandling == DuplicateHandling.AppendSuffix)
        {
            for (int i = 1; i <= 999; i++)
            {
                string candidate = $"{desired}_{i}";
                if (!_existingViewNames.Contains(candidate))
                {
                    _existingViewNames.Add(candidate);
                    return candidate;
                }
            }
        }

        // Skip/Overwrite both fall back to a guaranteed-unique suffix here, because the
        // view object already exists and must be given a non-colliding name.
        string fallback = $"{desired}_{Guid.NewGuid().ToString("N")[..6]}";
        _existingViewNames.Add(fallback);
        return fallback;
    }

    private static string Token(string template, string mark) =>
        template.Replace("{Mark}", mark);

    private string UniqueSheetNumber(string desired)
    {
        if (!_existingSheetNumbers.Contains(desired))
        {
            _existingSheetNumbers.Add(desired);
            return desired;
        }

        for (int i = 1; i <= 999; i++)
        {
            string candidate = $"{desired}-{i}";
            if (!_existingSheetNumbers.Contains(candidate))
            {
                _existingSheetNumbers.Add(candidate);
                return candidate;
            }
        }

        string fallback = $"{desired}-{Guid.NewGuid().ToString("N")[..6]}";
        _existingSheetNumbers.Add(fallback);
        return fallback;
    }

    private void ApplyTemplate(View view, string? templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            return;

        View? template = new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(v => v.IsTemplate
                && v.ViewType == view.ViewType
                && string.Equals(v.Name, templateName, StringComparison.OrdinalIgnoreCase));

        if (template is not null)
            view.ViewTemplateId = template.Id;
    }

    private ViewFamilyType FindViewFamilyType(ViewFamily family, string? preferredName)
    {
        List<ViewFamilyType> candidates = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .Where(t => t.ViewFamily == family)
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException($"No ViewFamilyType found for ViewFamily.{family}.");

        if (preferredName is not null)
        {
            ViewFamilyType? preferred = candidates.FirstOrDefault(t =>
                string.Equals(t.Name, preferredName, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return preferred;
        }

        return candidates[0];
    }
}
