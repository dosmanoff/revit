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

    // (generated view, names of that column's own elevations to keep) — section markers are
    // hidden in a final pass once every column's sections exist, so order can't leak markers.
    private readonly List<(ElementId ViewId, HashSet<string> KeepNames)> _markerCleanup = new();

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

        FinalizeSectionMarkers(result);
        return result;
    }

    /// <summary>
    /// After every column's views and sections exist, hides each generated view's foreign
    /// section markers (those whose view name isn't in that view's keep-set).
    /// </summary>
    private void FinalizeSectionMarkers(ViewCreationResult result)
    {
        if (_markerCleanup.Count == 0)
            return;

        try
        {
            using var tx = new Transaction(_doc, "Column Views — hide foreign sections");
            tx.Start();
            _doc.Regenerate();

            foreach ((ElementId viewId, HashSet<string> keep) in _markerCleanup)
            {
                if (_doc.GetElement(viewId) is View view)
                    HideForeignSectionMarkers(view, keep);
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            result.RecordError($"Hiding foreign sections: {ex.Message}");
        }
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

        // Rebar hosted by this column drives the elevation extents (dowels reaching above/
        // below the column) and the end-plan cut planes (first/last stirrup).
        List<Rebar> hostRebar = HostRebar(fi.Id);
        BoundingBoxXYZ rebarBox = CombineBoundingBoxes(bbox, hostRebar);
        (double? topStirrupZ, double? bottomStirrupZ) = StirrupZExtents(hostRebar);

        // World Z of each end-plan cut plane (above the relevant stirrup, else a face inset).
        double topCutZ = topStirrupZ.HasValue
            ? topStirrupZ.Value + _cfg.PlanCutAboveStirrup
            : bbox.Max.Z - _cfg.PlanCutInset;
        double bottomCutZ = bottomStirrupZ.HasValue
            ? bottomStirrupZ.Value + _cfg.PlanCutAboveStirrup
            : bbox.Min.Z + _cfg.PlanCutInset;
        var planCuts = new[] { (Z: topCutZ, Label: "Top Plan"), (Z: bottomCutZ, Label: "Bottom Plan") };

        var created = new List<View>();

        // Two perpendicular elevations, sized to enclose all of the column's rebar, with the
        // plan cut levels drawn across them.
        View? front = CreateElevation(rebarBox, lookDir: fwd, mark, "Front", planCuts);
        if (front is not null) created.Add(front);

        View? side = CreateElevation(rebarBox, lookDir: rgt, mark, "Side", planCuts);
        if (side is not null) created.Add(side);

        // Two end plans cut at the computed levels.
        View? topPlan = CreatePlan(fi, bbox, mark, isTop: true, topCutZ);
        if (topPlan is not null) created.Add(topPlan);

        View? botPlan = CreatePlan(fi, bbox, mark, isTop: false, bottomCutZ);
        if (botPlan is not null) created.Add(botPlan);

        // Optional isometric 3D view of just this column and its rebar.
        View3D? view3d = _cfg.Create3DView ? Create3D(rebarBox, mark) : null;
        if (view3d is not null) created.Add(view3d);

        foreach (View v in created)
            ApplyAppearance(v);

        // Visibility computed from the new crops before we query what's visible.
        _doc.Regenerate();

        // This column's own elevation markers; foreign markers are hidden in the final pass.
        HashSet<string> keepMarkerNames = created.OfType<ViewSection>()
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (View v in created)
        {
            ApplyForeignRebarTreatment(v, mark, result);
            _markerCleanup.Add((v.Id, keepMarkerNames));
            result.RecordCreated();
        }

        if (view3d is not null)
            Isolate3D(view3d, fi.Id, hostRebar);

        AssignScheduleMarks(hostRebar);

        List<ViewSchedule> schedules = BuildSchedules(mark, result);

        if (_cfg.PlaceOnSheet)
            PlaceOnSheet(fi, mark, created, schedules, result);

        tx.Commit();
    }

    private List<ViewSchedule> BuildSchedules(string mark, ViewCreationResult result)
    {
        var schedules = new List<ViewSchedule>();
        if (string.IsNullOrWhiteSpace(mark) || !_cfg.CreateRebarSchedule)
            return schedules;

        try
        {
            var builder = new ColumnScheduleBuilder(_doc);
            ViewSchedule s = builder.BuildRebarSchedule(
                mark,
                UniqueName(Token(_cfg.RebarScheduleNameTemplate, mark)),
                _cfg.BendingDetailGraphics);
            schedules.Add(s);
            result.RecordCreated();
        }
        catch (Exception ex)
        {
            result.RecordError($"Rebar schedule for {mark}: {ex.Message}");
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

    private View? CreateElevation(
        BoundingBoxXYZ bbox, XYZ lookDir, string mark, string direction,
        IReadOnlyList<(double Z, string Label)> planCuts)
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
        view.CropBoxActive = true;
        view.CropBoxVisible = false;

        Name(view, _cfg.ElevationNameTemplate, mark, direction: direction, end: null);
        ApplyTemplate(view, _cfg.ElevationViewTemplate);

        DrawPlanCutLines(view, center, basisX, halfWidth: hLocal + half, planCuts);
        return view;
    }

    /// <summary>
    /// Draws a horizontal detail line (with a label) across the elevation at each plan cut
    /// level, so the elevation shows where the top/bottom plans are taken. Best-effort.
    /// </summary>
    private void DrawPlanCutLines(
        View view, XYZ center, XYZ basisX, double halfWidth,
        IReadOnlyList<(double Z, string Label)> planCuts)
    {
        ElementId textTypeId = _doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

        foreach ((double z, string label) in planCuts)
        {
            XYZ rise = XYZ.BasisZ * (z - center.Z);
            XYZ p1 = center - basisX * halfWidth + rise;
            XYZ p2 = center + basisX * halfWidth + rise;

            try
            {
                _doc.Create.NewDetailCurve(view, Line.CreateBound(p1, p2));
                if (textTypeId != ElementId.InvalidElementId)
                    TextNote.Create(_doc, view.Id, p2, label, textTypeId);
            }
            catch (Exception)
            {
                // Annotation is non-critical — skip this cut line if Revit rejects it.
            }
        }
    }

    // -----------------------------------------------------------------------
    // End plans
    // -----------------------------------------------------------------------

    private View? CreatePlan(
        FamilyInstance fi, BoundingBoxXYZ bbox, string mark, bool isTop, double cutWorldZ)
    {
        Level? level = ResolveLevel(fi);
        if (level is null)
            throw new InvalidOperationException("cannot determine a base Level for the column.");

        ViewFamilyType vft = FindViewFamilyType(ViewFamily.FloorPlan, _cfg.PlanViewTypeName);
        ViewPlan plan = ViewPlan.Create(_doc, vft.Id, level.Id);

        double pad = _cfg.CropPadding;
        BoundingBoxXYZ existing = plan.CropBox;
        plan.CropBoxActive = true;
        plan.CropBoxVisible = false;
        plan.CropBox = new BoundingBoxXYZ
        {
            Min = new XYZ(bbox.Min.X - pad, bbox.Min.Y - pad, existing.Min.Z),
            Max = new XYZ(bbox.Max.X + pad, bbox.Max.Y + pad, existing.Max.Z),
        };

        double levelElev = level.ProjectElevation;
        double cut = cutWorldZ - levelElev;
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
    // 3D view (default isometric orientation, isolated to the column + its rebar)
    // -----------------------------------------------------------------------

    private View3D Create3D(BoundingBoxXYZ box, string mark)
    {
        ViewFamilyType vft = FindViewFamilyType(ViewFamily.ThreeDimensional, null);
        View3D view = View3D.CreateIsometric(_doc, vft.Id);

        double p = _cfg.CropPadding;
        view.SetSectionBox(new BoundingBoxXYZ
        {
            Min = new XYZ(box.Min.X - p, box.Min.Y - p, box.Min.Z - p),
            Max = new XYZ(box.Max.X + p, box.Max.Y + p, box.Max.Z + p),
        });
        view.IsSectionBoxActive = true;

        Name(view, _cfg.View3DNameTemplate, mark, direction: null, end: null);
        return view;
    }

    /// <summary>Hides everything in the 3D view except the column and its own rebar.</summary>
    private void Isolate3D(View3D view, ElementId columnId, IReadOnlyList<Rebar> hostRebar)
    {
        var keep = new HashSet<ElementId>(hostRebar.Select(r => r.Id)) { columnId };

        List<ElementId> toHide = new FilteredElementCollector(_doc, view.Id)
            .WhereElementIsNotElementType()
            .Where(e => !keep.Contains(e.Id) && e.CanBeHidden(view))
            .Select(e => e.Id)
            .ToList();

        if (toHide.Count > 0)
            HideSafely(view, toHide);
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

        HashSet<ElementId> foreign = new FilteredElementCollector(_doc, view.Id)
            .OfCategory(BuiltInCategory.OST_Rebar)
            .WhereElementIsNotElementType()
            .Where(e => IsForeignRebar(e, targetMark))
            .Select(e => e.Id)
            .ToHashSet();

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
            HideSafely(view, foreign.ToList());
        }

        // Always drop annotations (tags / dimensions) that point at foreign rebar.
        HideForeignAnnotations(view, foreign);
    }

    /// <summary>
    /// Hides section/elevation markers in the view whose view name is not in
    /// <paramref name="keepNames"/> — i.e. the markers belonging to other columns. A marker
    /// element's Name is its view's name, so this keeps this column's own elevation cut lines.
    /// </summary>
    private void HideForeignSectionMarkers(View view, HashSet<string> keepNames)
    {
        List<ElementId> toHide = new FilteredElementCollector(_doc, view.Id)
            .OfCategory(BuiltInCategory.OST_Viewers)
            .WhereElementIsNotElementType()
            .Where(e => !keepNames.Contains(e.Name))
            .Select(e => e.Id)
            .ToList();

        if (toHide.Count > 0)
            HideSafely(view, toHide);
    }

    /// <summary>Hides tags and dimensions in the view that reference any of <paramref name="foreignRebar"/>.</summary>
    private void HideForeignAnnotations(View view, HashSet<ElementId> foreignRebar)
    {
        var toHide = new List<ElementId>();

        foreach (IndependentTag tag in new FilteredElementCollector(_doc, view.Id)
                     .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
        {
            if (tag.GetTaggedLocalElementIds().Any(foreignRebar.Contains))
                toHide.Add(tag.Id);
        }

        foreach (Dimension dim in new FilteredElementCollector(_doc, view.Id)
                     .OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (ReferencesForeign(dim, foreignRebar))
                toHide.Add(dim.Id);
        }

        if (toHide.Count > 0)
            HideSafely(view, toHide);
    }

    private static bool ReferencesForeign(Dimension dim, HashSet<ElementId> foreignRebar)
    {
        ReferenceArray? refs = dim.References;
        if (refs is null) return false;

        foreach (Reference r in refs)
            if (r is not null && foreignRebar.Contains(r.ElementId))
                return true;

        return false;
    }

    private static void HideSafely(View view, IList<ElementId> ids)
    {
        try
        {
            view.HideElements(ids);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // One or more elements refused hiding — hide what we can, one at a time.
            foreach (ElementId id in ids)
            {
                try { view.HideElements(new List<ElementId> { id }); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { /* skip */ }
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

    private List<Rebar> HostRebar(ElementId columnId) =>
        new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rebar)
            .WhereElementIsNotElementType()
            .OfType<Rebar>()
            .Where(r => r.GetHostId() == columnId)
            .ToList();

    /// <summary>World AABB enclosing <paramref name="seed"/> and every rebar's bounding box.</summary>
    private static BoundingBoxXYZ CombineBoundingBoxes(BoundingBoxXYZ seed, IEnumerable<Rebar> rebar)
    {
        double minX = seed.Min.X, minY = seed.Min.Y, minZ = seed.Min.Z;
        double maxX = seed.Max.X, maxY = seed.Max.Y, maxZ = seed.Max.Z;

        foreach (Rebar r in rebar)
        {
            BoundingBoxXYZ? bb = r.get_BoundingBox(null);
            if (bb is null) continue;

            minX = Math.Min(minX, bb.Min.X); minY = Math.Min(minY, bb.Min.Y); minZ = Math.Min(minZ, bb.Min.Z);
            maxX = Math.Max(maxX, bb.Max.X); maxY = Math.Max(maxY, bb.Max.Y); maxZ = Math.Max(maxZ, bb.Max.Z);
        }

        return new BoundingBoxXYZ
        {
            Min = new XYZ(minX, minY, minZ),
            Max = new XYZ(maxX, maxY, maxZ),
        };
    }

    /// <summary>
    /// World Z of the highest and lowest stirrup/tie centres hosted by the column, or
    /// (null, null) when none are found. The 2025 API exposes no rebar-style accessor, so
    /// ties are detected geometrically: a tie is a flat horizontal loop whose vertical
    /// extent is well under its in-plan width, which excludes vertical longitudinals/dowels.
    /// </summary>
    private static (double? TopZ, double? BottomZ) StirrupZExtents(IEnumerable<Rebar> rebar)
    {
        double? top = null, bottom = null;

        foreach (Rebar r in rebar)
        {
            BoundingBoxXYZ? bb = r.get_BoundingBox(null);
            if (bb is null) continue;

            double zExt = bb.Max.Z - bb.Min.Z;
            double maxHoriz = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
            if (maxHoriz <= 0 || zExt >= 0.5 * maxHoriz) continue; // not a flat loop → skip

            double z = (bb.Min.Z + bb.Max.Z) / 2.0;
            top = top is null ? z : Math.Max(top.Value, z);
            bottom = bottom is null ? z : Math.Min(bottom.Value, z);
        }

        return (top, bottom);
    }

    /// <summary>
    /// Numbers the column's rebar Schedule Mark 1..N over unique (Type, Shape, Total Bar
    /// Length) groups, ordered by those keys. Best-effort: when the Schedule Mark parameter
    /// is read-only (Revit reinforcement numbering is automatic), the existing values are
    /// left untouched.
    /// </summary>
    private void AssignScheduleMarks(IReadOnlyList<Rebar> hostRebar)
    {
        if (hostRebar.Count == 0)
            return;

        var groups = hostRebar
            .Select(r => (Bar: r, Type: BarTypeName(r), Shape: BarShapeName(r), Len: BarTotalLength(r)))
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
                SetScheduleMark(x.Bar, mark);
        }
    }

    private static void SetScheduleMark(Rebar rebar, int mark)
    {
        Parameter? p = rebar.LookupParameter("Schedule Mark");
        if (p is null || p.IsReadOnly)
            return;

        if (p.StorageType == StorageType.Integer)
            p.Set(mark);
        else if (p.StorageType == StorageType.String)
            p.Set(mark.ToString());
    }

    private string BarTypeName(Rebar rebar) =>
        _doc.GetElement(rebar.GetTypeId())?.Name ?? string.Empty;

    private static string BarShapeName(Rebar rebar) =>
        rebar.LookupParameter("Shape")?.AsValueString() ?? string.Empty;

    private static double BarTotalLength(Rebar rebar) =>
        rebar.LookupParameter("Total Bar Length")?.AsDouble()
        ?? rebar.LookupParameter("Bar Length")?.AsDouble()
        ?? 0.0;

    private void ApplyAppearance(View view)
    {
        if (_cfg.ViewScale > 0)
            TrySet(() => view.Scale = _cfg.ViewScale);
        TrySet(() => view.DetailLevel = _cfg.DetailLevel);
        TrySet(() => view.DisplayStyle = _cfg.VisualStyle);
    }

    /// <summary>Applies a view setting, ignoring failures (e.g. when a view template owns it).</summary>
    private static void TrySet(Action set)
    {
        try { set(); }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
    }

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
