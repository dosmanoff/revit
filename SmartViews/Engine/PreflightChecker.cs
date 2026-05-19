using Autodesk.Revit.DB;
using SmartViews.Config;

namespace SmartViews.Engine;

public sealed record PreflightIssue(long ElementId, string ElementName, string Issue);

public static class PreflightChecker
{
    /// <summary>
    /// Inspects <paramref name="elementIds"/> against <paramref name="config"/> and returns
    /// any elements that are likely to fail or be silently skipped during the run.
    /// An empty list means the selection is clean.
    /// </summary>
    public static IReadOnlyList<PreflightIssue> Check(
        Document doc,
        IList<ElementId> elementIds,
        ViewConfig config)
    {
        bool needsLevel = config.ViewKinds.Any(k => k.Kind == ViewKind.Plan);
        var issues = new List<PreflightIssue>();

        foreach (ElementId id in elementIds)
        {
            Element? el = doc.GetElement(id);
            if (el is null)
            {
                issues.Add(new(id.Value, $"#{id.Value}", "Element not found in document."));
                continue;
            }

            string label = string.IsNullOrEmpty(el.Name) ? $"#{id.Value}" : $"{el.Name} (#{id.Value})";

            if (el.get_BoundingBox(null) is null)
                issues.Add(new(id.Value, label, "No bounding box — element will be skipped."));

            if (needsLevel && !HasResolvableLevel(doc, el))
                issues.Add(new(id.Value, label, "No associated level — plan views will fail."));
        }

        return issues;
    }

    private static bool HasResolvableLevel(Document doc, Element el)
    {
        if (el.LevelId != ElementId.InvalidElementId)
            return true;

        Parameter? hostParam = el.get_Parameter(BuiltInParameter.HOST_ID_PARAM);
        if (hostParam?.AsElementId() is { } hostId && hostId != ElementId.InvalidElementId)
        {
            Element? host = doc.GetElement(hostId);
            if (host?.LevelId != ElementId.InvalidElementId)
                return true;
        }

        return false;
    }
}
