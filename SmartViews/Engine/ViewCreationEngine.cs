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

    // Tracks names committed in this run (within-run dedup).
    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);

    // Per name-template sequence counter for {Index} token.
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

            // Overwrite: delete the existing view before creating the replacement.
            if (viewToDelete is not null)
                _doc.Delete(viewToDelete);

            View? view = kindConfig.Kind switch
            {
                ViewKind.Section     => CreateSection(paddedBbox, kindConfig),
                ViewKind.Plan        => CreatePlan(element, kindConfig),
                ViewKind.Isometric3D => CreateIsometric(paddedBbox, kindConfig),
                _ => throw new NotSupportedException($"ViewKind '{kindConfig.Kind}' is not supported."),
            };

            if (view is not null)
            {
                view.Name = viewName;
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

    private View? CreateSection(BoundingBoxXYZ paddedBbox, ViewKindConfig kindConfig)
    {
        ViewFamilyType vft = FindViewFamilyType(ViewFamily.Section, kindConfig.ViewFamilyTypeName);
        BoundingBoxXYZ sectionBox = BuildSectionBox(paddedBbox, kindConfig.SectionDirection);
        return ViewSection.CreateSection(_doc, vft.Id, sectionBox);
    }

    /// <summary>
    /// Builds a BoundingBoxXYZ whose Transform expresses the section's coordinate frame.
    ///
    /// Convention (confirmed against Revit API examples):
    ///   BasisX = right direction in the view
    ///   BasisY = up direction in the view (+Z for vertical sections)
    ///   BasisZ = BasisX × BasisY (right-handed); points toward the viewer
    ///   Origin = centre of the element bbox
    ///   Min/Max in local space cover the full element extent
    ///
    /// Local axis → world axis mapping per direction:
    ///   South (viewer -Y): localX=+X, localY=+Z, localZ=-Y
    ///   North (viewer +Y): localX=-X, localY=+Z, localZ=+Y
    ///   East  (viewer +X): localX=+Y, localY=+Z, localZ=+X
    ///   West  (viewer -X): localX=-Y, localY=+Z, localZ=-X
    /// </summary>
    private static BoundingBoxXYZ BuildSectionBox(BoundingBoxXYZ bbox, SectionDirection dir)
    {
        double midX = (bbox.Min.X + bbox.Max.X) / 2;
        double midY = (bbox.Min.Y + bbox.Max.Y) / 2;
        double midZ = (bbox.Min.Z + bbox.Max.Z) / 2;
        double extX = bbox.Max.X - bbox.Min.X;
        double extY = bbox.Max.Y - bbox.Min.Y;
        double extZ = bbox.Max.Z - bbox.Min.Z;

        XYZ basisX, basisY, basisZ;
        // Half-extents in local X, Y, Z
        double hLocal, vLocal, dLocal;

        switch (dir)
        {
            case SectionDirection.South: // viewer on -Y, looks +Y; localZ = -worldY
                basisX = XYZ.BasisX;
                basisY = XYZ.BasisZ;
                basisZ = new XYZ(0, -1, 0);
                hLocal = extX / 2; vLocal = extZ / 2; dLocal = extY / 2;
                break;

            case SectionDirection.North: // viewer on +Y, looks -Y; localZ = +worldY
                basisX = new XYZ(-1, 0, 0);
                basisY = XYZ.BasisZ;
                basisZ = XYZ.BasisY;
                hLocal = extX / 2; vLocal = extZ / 2; dLocal = extY / 2;
                break;

            case SectionDirection.East: // viewer on +X, looks -X; localZ = +worldX
                basisX = XYZ.BasisY;
                basisY = XYZ.BasisZ;
                basisZ = XYZ.BasisX;
                hLocal = extY / 2; vLocal = extZ / 2; dLocal = extX / 2;
                break;

            default: // West: viewer on -X, looks +X; localZ = -worldX
                basisX = new XYZ(0, -1, 0);
                basisY = XYZ.BasisZ;
                basisZ = new XYZ(-1, 0, 0);
                hLocal = extY / 2; vLocal = extZ / 2; dLocal = extX / 2;
                break;
        }

        var transform = new Transform(Transform.Identity)
        {
            Origin = new XYZ(midX, midY, midZ),
            BasisX = basisX,
            BasisY = basisY,
            BasisZ = basisZ,
        };

        return new BoundingBoxXYZ
        {
            Transform = transform,
            Min = new XYZ(-hLocal, -vLocal, -dLocal),
            Max = new XYZ( hLocal,  vLocal,  dLocal),
        };
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

        // Crop to the element's padded bounding box projected onto the plan.
        // For unrotated plans the view CS matches world CS in X and Y; Z is the view range depth.
        BoundingBoxXYZ paddedBbox = ApplyOffset(element.get_BoundingBox(null)!, _config.CropOffset);
        BoundingBoxXYZ existingCrop = planView.CropBox;

        planView.CropBoxActive = true;
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

        // Hosted elements (doors, windows, …) — walk up to the host's level.
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

            // Preferred name specified but not found — fall through to first match rather than throw.
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
        // Increment per-template counter before substitution so {Index} is never 0.
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

    /// <summary>
    /// Checks <paramref name="name"/> against existing and in-run names.
    /// Returns true if the caller should proceed; may modify <paramref name="name"/>
    /// (AppendSuffix case). Sets <paramref name="viewToDelete"/> when the caller must
    /// delete an existing view inside its Transaction before creating the replacement.
    /// </summary>
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
