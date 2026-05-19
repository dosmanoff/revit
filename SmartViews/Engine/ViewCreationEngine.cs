using Autodesk.Revit.DB;
using SmartViews.Config;

namespace SmartViews.Engine;

/// <summary>
/// Orchestrates batch view creation for a set of elements.
/// Each public method maps to one view kind; Run() dispatches based on config.
/// </summary>
public sealed class ViewCreationEngine
{
    private readonly Document _doc;
    private readonly ViewConfig _config;

    // Tracks names already created in this run to detect within-run duplicates.
    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);

    public ViewCreationEngine(Document doc, ViewConfig config)
    {
        _doc = doc;
        _config = config;
    }

    /// <summary>
    /// Entry point — iterates selected elements and creates the requested view kinds.
    /// Must be called inside an open TransactionGroup.
    /// </summary>
    public ViewCreationResult Run(IList<ElementId> elementIds)
    {
        var result = new ViewCreationResult();

        // Pre-load existing view names so we can detect duplicates against the model.
        var existingNames = CollectExistingViewNames();

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

    private void ProcessElement(
        Element element,
        HashSet<string> existingNames,
        ViewCreationResult result)
    {
        BoundingBoxXYZ? bbox = element.get_BoundingBox(null);
        if (bbox is null)
        {
            result.RecordError($"Element {element.Id.Value} has no bounding box — skipped.");
            return;
        }

        BoundingBoxXYZ paddedBbox = ApplyOffset(bbox, _config.CropOffset);

        foreach (ViewKindConfig kindConfig in _config.ViewKinds)
        {
            string viewName = ResolveViewName(element, kindConfig.NameTemplate, result);
            if (viewName is null!)
                continue;

            if (!EnsureUniqueName(viewName, existingNames, _config.DuplicateHandling, result))
                continue;

            using var tx = new Transaction(_doc, $"Create view: {viewName}");
            tx.Start();

            View? view = kindConfig.Kind switch
            {
                ViewKind.Section => CreateSection(paddedBbox, kindConfig),
                ViewKind.Plan => CreatePlan(element, kindConfig),
                ViewKind.Isometric3D => CreateIsometric(kindConfig),
                _ => throw new NotSupportedException($"View kind '{kindConfig.Kind}' is not supported."),
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

    // -------------------------------------------------------------------------
    // View factories — stubs to be completed in later phases
    // -------------------------------------------------------------------------

    private View? CreateSection(BoundingBoxXYZ bbox, ViewKindConfig kindConfig)
    {
        // TODO: build a Transform oriented along the element's dominant face or
        // a user-selected direction, then call ViewSection.CreateSection().
        throw new NotImplementedException("Section creation is not yet implemented.");
    }

    private View? CreatePlan(Element element, ViewKindConfig kindConfig)
    {
        // TODO: resolve the host Level, find a matching FloorPlan ViewFamilyType,
        // then call ViewPlan.Create().
        throw new NotImplementedException("Plan creation is not yet implemented.");
    }

    private View? CreateIsometric(ViewKindConfig kindConfig)
    {
        // TODO: find a suitable ViewFamilyType for 3D views, then call
        // View3D.CreateIsometric().
        throw new NotImplementedException("3-D isometric creation is not yet implemented.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static BoundingBoxXYZ ApplyOffset(BoundingBoxXYZ bbox, double offset)
    {
        var result = new BoundingBoxXYZ
        {
            Min = new XYZ(bbox.Min.X - offset, bbox.Min.Y - offset, bbox.Min.Z - offset),
            Max = new XYZ(bbox.Max.X + offset, bbox.Max.Y + offset, bbox.Max.Z + offset),
        };
        return result;
    }

    private static string ResolveViewName(
        Element element,
        string template,
        ViewCreationResult result)
    {
        // Token substitution: {Mark}, {Level}, {Type}, {Index}
        string name = template;

        string mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
        string typeName = element.Document.GetElement(element.GetTypeId())?.Name ?? string.Empty;
        string levelName = element.LevelId != ElementId.InvalidElementId
            ? (element.Document.GetElement(element.LevelId)?.Name ?? string.Empty)
            : string.Empty;

        name = name
            .Replace("{Mark}", mark)
            .Replace("{Type}", typeName)
            .Replace("{Level}", levelName);

        // {Index} is resolved later when we know the sequence; strip for now.
        name = name.Replace("{Index}", string.Empty);

        if (string.IsNullOrWhiteSpace(name))
        {
            result.RecordError($"Element {element.Id.Value}: name template resolved to empty string.");
            return null!;
        }

        return name.Trim();
    }

    private bool EnsureUniqueName(
        string name,
        HashSet<string> existingNames,
        DuplicateHandling handling,
        ViewCreationResult result)
    {
        if (!existingNames.Contains(name) && !_usedNames.Contains(name))
            return true;

        return handling switch
        {
            DuplicateHandling.Skip => Skip(name, result),
            DuplicateHandling.Overwrite => true,   // caller deletes or renames existing view — TODO
            DuplicateHandling.AppendSuffix => true, // suffix logic — TODO
            _ => Skip(name, result),
        };

        static bool Skip(string n, ViewCreationResult r)
        {
            r.RecordSkipped();
            return false;
        }
    }

    private HashSet<string> CollectExistingViewNames()
    {
        return new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
