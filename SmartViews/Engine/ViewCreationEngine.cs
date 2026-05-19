using Autodesk.Revit.DB;
using SmartViews.Config;

namespace SmartViews.Engine;

/// <summary>
/// Orchestrates batch view creation for a set of elements.
/// Must be called inside an open TransactionGroup.
/// </summary>
public sealed class ViewCreationEngine
{
    private readonly Document _doc;
    private readonly ViewConfig _config;

    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _templateCounters = new();

    public ViewCreationEngine(Document doc, ViewConfig config)
    {
        _doc = doc;
        _config = config;
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

        BoundingBoxXYZ paddedBbox = ApplyOffset(rawBbox, _config.CropOffset);

        foreach (ViewKindConfig kindConfig in _config.ViewKinds)
        {
            string? viewName = ResolveViewName(element, kindConfig, result);
            if (viewName is null)
                continue;

            if (!ResolveUniqueName(ref viewName, existingNames, _config.DuplicateHandling, result,
                    out ElementId? viewToDelete))
                continue;

            using var tx = new Transaction(_doc, $"SmartViews: {viewName}");
            tx.Start();

            if (viewToDelete is not null)
                _doc.Delete(viewToDelete);

            View? view = kindConfig.Kind switch
            {
                ViewKind.Section     => CreateSection(element, paddedBbox, kindConfig),
                ViewKind.Plan        => CreatePlan(element, kindConfig),
                ViewKind.Isometric3D => CreateIsometric(paddedBbox, kindConfig),
                _ => throw new NotSupportedException($"ViewKind '{kindConfig.Kind}' is not supported."),
            };

            if (view is not null)
            {
                view.Name = viewName;
                ApplyViewTemplate(view, kindConfig.ViewTemplateName);
                existingNames.Add(viewName);
                _usedNames.Add(viewName);
                result.RecordCreated();
            }

            tx.Commit();
        }
    }

    // -----------------------------------------------------------------------
    // Section
    // -----------------------------------------------------------------------

    private View? CreateSection(Element element, BoundingBoxXYZ paddedBbox, ViewKindConfig kindConfig)
    {
        ViewFamilyType vft = FindViewFamilyType(ViewFamily.Section, kindConfig.ViewFamilyTypeName);

        (XYZ fwd, XYZ rgt) = kindConfig.AlignToElement
            ? GetElementOrientation(element)
            : (XYZ.BasisY, XYZ.BasisX);

        BoundingBoxXYZ sectionBox = BuildSectionBox(paddedBbox, kindConfig.SectionDirection, fwd, rgt);
        return ViewSection.CreateSection(_doc, vft.Id, sectionBox);
    }

    /// <summary>
    /// Builds the section BoundingBoxXYZ from the padded element bbox, a viewing direction,
    /// and the element's orientation vectors.
    ///
    /// Coordinate frame convention (matches Revit API examples):
    ///   BasisX = right direction in the rendered view
    ///   BasisY = up direction (+Z)
    ///   BasisZ = BasisX × BasisY (right-handed) — points toward the viewer
    ///   Origin = centre of the padded bbox
    ///
    /// For each SectionDirection, given element forward (fwd) and right (rgt):
    ///   South → BasisX = +rgt,  BasisZ = −fwd  (viewer behind the "front face")
    ///   North → BasisX = −rgt,  BasisZ = +fwd
    ///   East  → BasisX = +fwd,  BasisZ = +rgt  (viewer on the "right side")
    ///   West  → BasisX = −fwd,  BasisZ = −rgt
    ///
    /// Half-extents are computed via AABB projection so rotated elements produce
    /// a correctly-sized box even when the bbox axes don't align with the view axes.
    /// </summary>
    private static BoundingBoxXYZ BuildSectionBox(
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
            _                      => -fwd,   // West
        };

        XYZ basisZ = dir switch
        {
            SectionDirection.South => -fwd,
            SectionDirection.North =>  fwd,
            SectionDirection.East  =>  rgt,
            _                      => -rgt,   // West
        };

        // For an AABB, the half-extent along an arbitrary unit direction u is:
        //   |u.X|·halfX  +  |u.Y|·halfY  +  |u.Z|·halfZ
        double hLocal = HalfExtentAlongAxis(bbox, basisX);
        double vLocal = (bbox.Max.Z - bbox.Min.Z) / 2;       // always world-Z
        double dLocal = HalfExtentAlongAxis(bbox, basisZ);

        double midX = (bbox.Min.X + bbox.Max.X) / 2;
        double midY = (bbox.Min.Y + bbox.Max.Y) / 2;
        double midZ = (bbox.Min.Z + bbox.Max.Z) / 2;

        var transform = new Transform(Transform.Identity)
        {
            Origin = new XYZ(midX, midY, midZ),
            BasisX = basisX,
            BasisY = XYZ.BasisZ,
            BasisZ = basisZ,
        };

        return new BoundingBoxXYZ
        {
            Transform = transform,
            Min = new XYZ(-hLocal, -vLocal, -dLocal),
            Max = new XYZ( hLocal,  vLocal,  dLocal),
        };
    }

    private static double HalfExtentAlongAxis(BoundingBoxXYZ bbox, XYZ axis)
    {
        double halfX = (bbox.Max.X - bbox.Min.X) / 2;
        double halfY = (bbox.Max.Y - bbox.Min.Y) / 2;
        double halfZ = (bbox.Max.Z - bbox.Min.Z) / 2;
        return Math.Abs(axis.X) * halfX
             + Math.Abs(axis.Y) * halfY
             + Math.Abs(axis.Z) * halfZ;
    }

    /// <summary>
    /// Returns the element's (forward, right) unit vectors in the XY plane.
    ///
    /// Priority:
    ///   1. LocationCurve  → direction along the curve  (walls, beams, pipes, …)
    ///   2. FamilyInstance → FacingOrientation           (doors, windows, columns, …)
    ///   3. World axes     → (BasisY, BasisX)            (fallback)
    ///
    /// "right" is always "forward rotated 90° clockwise in XY", which satisfies
    /// rgt × BasisZ = −fwd (required for the right-handed section frames above).
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

        // 90° CW rotation in XY: (x, y) → (y, −x)
        var right = new XYZ(fwd.Y, -fwd.X, 0);
        return (fwd, right);
    }

    // -----------------------------------------------------------------------
    // Plan
    // -----------------------------------------------------------------------

    private View? CreatePlan(Element element, ViewKindConfig kindConfig)
    {
        Level level = GetHostLevel(element)
            ?? throw new InvalidOperationException(
                $"Cannot determine Level for element {element.Id.Value}.");

        ViewFamilyType vft = FindViewFamilyType(ViewFamily.FloorPlan, kindConfig.ViewFamilyTypeName);
        ViewPlan planView = ViewPlan.Create(_doc, vft.Id, level.Id);

        BoundingBoxXYZ paddedBbox = ApplyOffset(element.get_BoundingBox(null)!, _config.CropOffset);
        BoundingBoxXYZ existingCrop = planView.CropBox;

        planView.CropBoxActive  = true;
        planView.CropBoxVisible = true;
        planView.CropBox = new BoundingBoxXYZ
        {
            Min = new XYZ(paddedBbox.Min.X, paddedBbox.Min.Y, existingCrop.Min.Z),
            Max = new XYZ(paddedBbox.Max.X, paddedBbox.Max.Y, existingCrop.Max.Z),
        };

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
            if (host?.LevelId != ElementId.InvalidElementId)
                return _doc.GetElement(host.LevelId) as Level;
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // 3-D Isometric
    // -----------------------------------------------------------------------

    private View? CreateIsometric(BoundingBoxXYZ paddedBbox, ViewKindConfig kindConfig)
    {
        ViewFamilyType vft = FindViewFamilyType(ViewFamily.ThreeDimensional, kindConfig.ViewFamilyTypeName);
        View3D view3d = View3D.CreateIsometric(_doc, vft.Id);
        view3d.SetSectionBox(paddedBbox);
        view3d.IsSectionBoxActive = true;
        return view3d;
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
                $"No ViewFamilyType found for ViewFamily.{family}. " +
                "Ensure the project template includes one.");

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
    // Name resolution
    // -----------------------------------------------------------------------

    private string? ResolveViewName(
        Element element,
        ViewKindConfig kindConfig,
        ViewCreationResult result)
    {
        string template = kindConfig.NameTemplate;
        _templateCounters.TryGetValue(template, out int idx);
        _templateCounters[template] = ++idx;

        string mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
        string typeName = element.Document.GetElement(element.GetTypeId())?.Name ?? string.Empty;
        string levelName = element.LevelId != ElementId.InvalidElementId
            ? (element.Document.GetElement(element.LevelId)?.Name ?? string.Empty)
            : string.Empty;

        string name = template
            .Replace("{Mark}",  mark)
            .Replace("{Type}",  typeName)
            .Replace("{Level}", levelName)
            .Replace("{Index}", idx.ToString())
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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static BoundingBoxXYZ ApplyOffset(BoundingBoxXYZ bbox, double offset) =>
        new()
        {
            Min = new XYZ(bbox.Min.X - offset, bbox.Min.Y - offset, bbox.Min.Z - offset),
            Max = new XYZ(bbox.Max.X + offset, bbox.Max.Y + offset, bbox.Max.Z + offset),
        };

    private HashSet<string> CollectExistingViewNames() =>
        new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
