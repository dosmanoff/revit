using Autodesk.Revit.DB;
using SmartViews.Config;
using View = Autodesk.Revit.DB.View;

namespace SmartViews.Engine;

/// <summary>
/// Orchestrates batch view creation for a set of elements.
/// Must be called inside an open TransactionGroup.
/// </summary>
public sealed class ViewCreationEngine
{
    private readonly Document _doc;
    private readonly ViewConfig _config;
    private readonly CropOffsets _offsets;

    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _templateCounters = new();

    public ViewCreationEngine(Document doc, ViewConfig config)
    {
        _doc = doc;
        _config = config;
        _offsets = config.Offsets ?? new CropOffsets();
    }

    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    public ViewCreationResult Run(IList<ElementId> elementIds)
    {
        var result = new ViewCreationResult();
        HashSet<string> existingNames = CollectExistingViewNames();

        foreach (ElementId id in elementIds)
        {
            Element? element = _doc.GetElement(id);
            if (element is null)
            {
                result.RecordError($"Element {id.Value} not found.");
                continue;
            }

            try
            {
                ProcessElement(element, existingNames, result);
            }
            catch (Exception ex)
            {
                result.RecordError($"Element {id.Value} ({element.Name}): {ex.Message}");
            }
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // Per-element dispatch
    // -----------------------------------------------------------------------

    private void ProcessElement(
        Element element,
        HashSet<string> existingNames,
        ViewCreationResult result)
    {
        BoundingBoxXYZ? rawBbox = element.get_BoundingBox(null);
        if (rawBbox is null)
        {
            result.RecordError($"Element {element.Id.Value} has no bounding box — skipped.");
            return;
        }

        foreach (ViewKindConfig kindConfig in _config.ViewKinds)
        {
            foreach (SectionDirection dir in EffectiveDirections(kindConfig))
            {
                string? sheetNumber = kindConfig.SheetTarget?.SheetNumber;
                string? viewName = ResolveViewName(element, kindConfig, dir, sheetNumber, result);
                if (viewName is null)
                    continue;

                if (!ResolveUniqueName(ref viewName, existingNames, _config.DuplicateHandling, result,
                        out ElementId? viewToDelete))
                    continue;

                using var tx = new Transaction(_doc, $"SmartViews: {viewName}");
                tx.Start();

                if (viewToDelete is not null)
                    DeleteViewWithViewports(viewToDelete);

                View? view = kindConfig.Kind switch
                {
                    ViewKind.Section     => CreateSection(element, rawBbox, kindConfig, dir),
                    ViewKind.Plan        => CreatePlan(element, rawBbox, kindConfig),
                    ViewKind.Isometric3D => CreateIsometric(rawBbox, kindConfig),
                    _ => throw new NotSupportedException($"ViewKind '{kindConfig.Kind}' is not supported."),
                };

                if (view is not null)
                {
                    view.Name = viewName;
                    ApplyViewTemplate(view, kindConfig.ViewTemplateName);

                    if (kindConfig.SheetTarget is { SheetNumber: { } sn } target
                        && !string.IsNullOrWhiteSpace(sn))
                    {
                        PlaceOnSheet(view, target, result);
                    }

                    existingNames.Add(viewName);
                    _usedNames.Add(viewName);
                    result.RecordCreated();
                }

                tx.Commit();
            }
        }
    }

    private static IEnumerable<SectionDirection> EffectiveDirections(ViewKindConfig k) =>
        k.Kind == ViewKind.Section && k.CreateAllDirections
            ? Enum.GetValues<SectionDirection>()
            : [k.SectionDirection];

    // -----------------------------------------------------------------------
    // Section  (D1: elevation-style — origin at near face)
    // -----------------------------------------------------------------------

    private View? CreateSection(
        Element element,
        BoundingBoxXYZ rawBbox,
        ViewKindConfig kindConfig,
        SectionDirection dir)
    {
        ViewFamilyType vft = FindViewFamilyType(ViewFamily.Section, kindConfig.ViewFamilyTypeName);

        (XYZ fwd, XYZ rgt) = kindConfig.AlignToElement
            ? GetElementOrientation(element)
            : (XYZ.BasisY, XYZ.BasisX);

        BoundingBoxXYZ sectionBox = BuildSectionBox(rawBbox, dir, fwd, rgt);
        return ViewSection.CreateSection(_doc, vft.Id, sectionBox);
    }

    /// <summary>
    /// Builds the section BoundingBoxXYZ with an elevation-style near-face origin (D1).
    ///
    /// Coordinate frame: BasisX = right, BasisY = world Z (up), BasisZ = toward viewer.
    /// The transform origin is placed at the near face of the element's AABB so the
    /// cut plane (local Z = 0) sits just outside the near face, with Max.Z = Near (small
    /// positive gap) and Min.Z = -(fullDepth + Far).
    ///
    /// Direction → (BasisX, BasisZ):
    ///   South  (+rgt, −fwd)   North  (−rgt, +fwd)
    ///   East   (+fwd, +rgt)   West   (−fwd, −rgt)
    /// </summary>
    private BoundingBoxXYZ BuildSectionBox(
        BoundingBoxXYZ bbox,
        SectionDirection dir,
        XYZ fwd,
        XYZ rgt)
    {
        XYZ basisX = dir switch
        {
            SectionDirection.South =>  rgt,
            SectionDirection.North => -rgt,
            SectionDirection.East  =>  fwd,
            _                      => -fwd,
        };

        XYZ basisZ = dir switch
        {
            SectionDirection.South => -fwd,
            SectionDirection.North =>  fwd,
            SectionDirection.East  =>  rgt,
            _                      => -rgt,
        };

        double hLocal    = HalfExtentAlongAxis(bbox, basisX);
        double vLocal    = (bbox.Max.Z - bbox.Min.Z) / 2.0;
        double dLocal    = HalfExtentAlongAxis(bbox, basisZ);   // half-depth of element

        double midX = (bbox.Min.X + bbox.Max.X) / 2.0;
        double midY = (bbox.Min.Y + bbox.Max.Y) / 2.0;
        double midZ = (bbox.Min.Z + bbox.Max.Z) / 2.0;

        // Near-face centre (closest face to viewer in BasisZ direction).
        XYZ bboxCenter   = new(midX, midY, midZ);
        XYZ nearFaceCenter = bboxCenter + dLocal * basisZ;

        var transform = new Transform(Transform.Identity)
        {
            Origin = nearFaceCenter,
            BasisX = basisX,
            BasisY = XYZ.BasisZ,
            BasisZ = basisZ,
        };

        double fullDepth = 2.0 * dLocal;

        return new BoundingBoxXYZ
        {
            Transform = transform,
            Min = new XYZ(-hLocal - _offsets.Left,   -vLocal - _offsets.Bottom, -(fullDepth + _offsets.Far)),
            Max = new XYZ( hLocal + _offsets.Right,   vLocal + _offsets.Top,     _offsets.Near),
        };
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

    /// <summary>
    /// Returns (forward, right) unit vectors for the element.
    /// Priority: LocationCurve → FacingOrientation (FamilyInstance) → world axes.
    /// right = forward rotated 90° clockwise in XY.
    /// </summary>
    private static (XYZ Forward, XYZ Right) GetElementOrientation(Element element)
    {
        XYZ? fwd = null;

        if (element.Location is LocationCurve locCurve)
        {
            Curve curve = locCurve.Curve;
            XYZ dir = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
            var flat = new XYZ(dir.X, dir.Y, 0);
            if (flat.GetLength() > 1e-6)
                fwd = flat.Normalize();
        }
        else if (element is FamilyInstance fi)
        {
            var flat = new XYZ(fi.FacingOrientation.X, fi.FacingOrientation.Y, 0);
            if (flat.GetLength() > 1e-6)
                fwd = flat.Normalize();
        }

        if (fwd is null)
            return (XYZ.BasisY, XYZ.BasisX);

        var right = new XYZ(fwd.Y, -fwd.X, 0);   // 90° CW in XY
        return (fwd, right);
    }

    // -----------------------------------------------------------------------
    // Plan  (D4: Left/Right/Top/Bottom offsets)
    // -----------------------------------------------------------------------

    private View? CreatePlan(Element element, BoundingBoxXYZ rawBbox, ViewKindConfig kindConfig)
    {
        Level level = GetHostLevel(element)
            ?? throw new InvalidOperationException(
                $"Cannot determine Level for element {element.Id.Value}.");

        ViewFamilyType vft = FindViewFamilyType(ViewFamily.FloorPlan, kindConfig.ViewFamilyTypeName);
        ViewPlan planView = ViewPlan.Create(_doc, vft.Id, level.Id);

        BoundingBoxXYZ existingCrop = planView.CropBox;

        planView.CropBoxActive  = true;
        planView.CropBoxVisible = true;
        planView.CropBox = new BoundingBoxXYZ
        {
            Min = new XYZ(rawBbox.Min.X - _offsets.Left,  rawBbox.Min.Y - _offsets.Bottom, existingCrop.Min.Z),
            Max = new XYZ(rawBbox.Max.X + _offsets.Right, rawBbox.Max.Y + _offsets.Top,    existingCrop.Max.Z),
        };

        if (_config.PlanViewRange is { } vrCfg)
        {
            Autodesk.Revit.DB.PlanViewRange viewRange = planView.GetViewRange();
            viewRange.SetLevelId(PlanViewPlane.TopClipPlane,    level.Id);
            viewRange.SetOffset(PlanViewPlane.TopClipPlane,     vrCfg.TopOffset);
            viewRange.SetLevelId(PlanViewPlane.CutPlane,        level.Id);
            viewRange.SetOffset(PlanViewPlane.CutPlane,         vrCfg.CutOffset);
            viewRange.SetLevelId(PlanViewPlane.BottomClipPlane, level.Id);
            viewRange.SetOffset(PlanViewPlane.BottomClipPlane,  vrCfg.BottomOffset);
            viewRange.SetLevelId(PlanViewPlane.ViewDepthPlane,  level.Id);
            viewRange.SetOffset(PlanViewPlane.ViewDepthPlane,   vrCfg.ViewDepth);
            planView.SetViewRange(viewRange);
        }

        return planView;
    }

    private Level? GetHostLevel(Element element)
    {
        if (element.LevelId != ElementId.InvalidElementId)
            return _doc.GetElement(element.LevelId) as Level;

        Parameter? hostParam = element.get_Parameter(BuiltInParameter.HOST_ID_PARAM);
        if (hostParam?.AsElementId() is { } hostId
            && hostId != ElementId.InvalidElementId)
        {
            Element? host = _doc.GetElement(hostId);
            if (host is not null && host.LevelId != ElementId.InvalidElementId)
                return _doc.GetElement(host.LevelId) as Level;
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // 3-D Isometric  (D4: Left/Right = X, Top/Bottom = Z, Far = Y symmetric)
    // -----------------------------------------------------------------------

    private View? CreateIsometric(BoundingBoxXYZ rawBbox, ViewKindConfig kindConfig)
    {
        ViewFamilyType vft = FindViewFamilyType(ViewFamily.ThreeDimensional, kindConfig.ViewFamilyTypeName);
        View3D view3d = View3D.CreateIsometric(_doc, vft.Id);

        var sectionBox = new BoundingBoxXYZ
        {
            Min = new XYZ(rawBbox.Min.X - _offsets.Left,   rawBbox.Min.Y - _offsets.Far,    rawBbox.Min.Z - _offsets.Bottom),
            Max = new XYZ(rawBbox.Max.X + _offsets.Right,  rawBbox.Max.Y + _offsets.Far,    rawBbox.Max.Z + _offsets.Top),
        };

        view3d.SetSectionBox(sectionBox);
        view3d.IsSectionBoxActive = true;
        return view3d;
    }

    // -----------------------------------------------------------------------
    // Sheet placement  (Phase 3.1)
    // -----------------------------------------------------------------------

    private void PlaceOnSheet(View view, SheetTarget target, ViewCreationResult result)
    {
        ViewSheet? sheet = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .FirstOrDefault(s => string.Equals(
                s.SheetNumber, target.SheetNumber, StringComparison.OrdinalIgnoreCase));

        if (sheet is null)
        {
            result.RecordError(
                $"Sheet \"{target.SheetNumber}\" not found — view \"{view.Name}\" was not placed.");
            return;
        }

        if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id))
        {
            result.RecordError(
                $"Cannot place view \"{view.Name}\" on sheet \"{target.SheetNumber}\" " +
                "(already placed or view type not allowed).");
            return;
        }

        BoundingBoxUV outline = sheet.Outline;
        XYZ center = target.ViewportCenter is { } pt
            ? new XYZ(pt.X, pt.Y, 0)
            : new XYZ((outline.Min.U + outline.Max.U) / 2,
                      (outline.Min.V + outline.Max.V) / 2, 0);

        Viewport vp = Viewport.Create(_doc, sheet.Id, view.Id, center);

        if (!string.IsNullOrWhiteSpace(target.ViewportTypeName))
        {
            ElementId? typeId = new FilteredElementCollector(_doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .Where(t => t.GetType().Name == "ViewportType")
                .FirstOrDefault(t => string.Equals(
                    t.Name, target.ViewportTypeName, StringComparison.OrdinalIgnoreCase))
                ?.Id;

            if (typeId is not null)
                vp.ChangeTypeId(typeId);
        }
    }

    // -----------------------------------------------------------------------
    // View template
    // -----------------------------------------------------------------------

    private void ApplyViewTemplate(View view, string? templateName)
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

    // -----------------------------------------------------------------------
    // ViewFamilyType lookup
    // -----------------------------------------------------------------------

    private ViewFamilyType FindViewFamilyType(ViewFamily family, string? preferredName)
    {
        List<ViewFamilyType> candidates = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .Where(t => t.ViewFamily == family)
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"No ViewFamilyType found for ViewFamily.{family}.");

        if (preferredName is not null)
        {
            ViewFamilyType? preferred = candidates.FirstOrDefault(t =>
                string.Equals(t.Name, preferredName, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return preferred;
        }

        return candidates[0];
    }

    // -----------------------------------------------------------------------
    // Name resolution  (Phase 3.2: {Sheet} token)
    // -----------------------------------------------------------------------

    private string? ResolveViewName(
        Element element,
        ViewKindConfig kindConfig,
        SectionDirection dir,
        string? sheetNumber,
        ViewCreationResult result)
    {
        string template = kindConfig.NameTemplate;
        _templateCounters.TryGetValue(template, out int idx);
        _templateCounters[template] = ++idx;

        string mark      = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
        string typeName  = element.Document.GetElement(element.GetTypeId())?.Name ?? string.Empty;
        string levelName = element.LevelId != ElementId.InvalidElementId
            ? (element.Document.GetElement(element.LevelId)?.Name ?? string.Empty)
            : string.Empty;

        string name = template
            .Replace("{Mark}",      mark)
            .Replace("{Type}",      typeName)
            .Replace("{Level}",     levelName)
            .Replace("{Index}",     idx.ToString())
            .Replace("{Direction}", dir.ToString())
            .Replace("{Sheet}",     sheetNumber ?? string.Empty)
            .Trim();

        if (string.IsNullOrEmpty(name))
        {
            result.RecordError(
                $"Element {element.Id.Value}: name template \"{template}\" resolved to an empty string.");
            return null;
        }

        return name;
    }

    // -----------------------------------------------------------------------
    // Duplicate handling
    // -----------------------------------------------------------------------

    private bool ResolveUniqueName(
        ref string name,
        HashSet<string> existingNames,
        DuplicateHandling handling,
        ViewCreationResult result,
        out ElementId? viewToDelete)
    {
        viewToDelete = null;

        if (!IsDuplicate(name, existingNames))
            return true;

        switch (handling)
        {
            case DuplicateHandling.Skip:
                result.RecordSkipped();
                return false;

            case DuplicateHandling.Overwrite:
                viewToDelete = FindViewIdByName(name);
                return true;

            case DuplicateHandling.AppendSuffix:
                for (int i = 1; i <= 999; i++)
                {
                    string candidate = $"{name}_{i}";
                    if (!IsDuplicate(candidate, existingNames))
                    {
                        name = candidate;
                        return true;
                    }
                }
                result.RecordError($"Could not find a unique suffix for \"{name}\" after 999 attempts.");
                return false;

            default:
                result.RecordSkipped();
                return false;
        }
    }

    private bool IsDuplicate(string name, HashSet<string> existingNames)
        => existingNames.Contains(name) || _usedNames.Contains(name);

    private ElementId? FindViewIdByName(string name) =>
        new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Id;

    /// <summary>
    /// Removes all viewports that reference <paramref name="viewId"/>, then deletes the view.
    /// Required before overwriting a view that is already placed on a sheet.
    /// </summary>
    private void DeleteViewWithViewports(ElementId viewId)
    {
        List<ElementId> viewportIds = new FilteredElementCollector(_doc)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .Where(vp => vp.ViewId == viewId)
            .Select(vp => vp.Id)
            .ToList();

        foreach (ElementId vpId in viewportIds)
            _doc.Delete(vpId);

        _doc.Delete(viewId);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private HashSet<string> CollectExistingViewNames() =>
        new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
