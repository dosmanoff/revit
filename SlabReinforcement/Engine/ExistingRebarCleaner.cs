using Autodesk.Revit.DB;
using SlabReinforcement.Domain;

namespace SlabReinforcement.Engine;

/// <summary>
/// Finds rebar previously placed by this plugin on a slab (tagged via the <c>Comments</c>
/// parameter as <c>SR:{configName}:{slabId}:{layer}</c>) and deletes it, so a re-run of the
/// same config is idempotent. Cleaning matches the <c>SR:{config}:{slabId}:</c> prefix
/// across all layers.
/// </summary>
public static class ExistingRebarCleaner
{
    public const string TagPrefix = "SR:";

    public static string MakeTag(string configName, ElementId slabId, SlabLayer layer) =>
        $"{TagPrefix}{configName}:{slabId.Value}:{layer}";

    public static string PrefixFor(string configName, ElementId slabId) =>
        $"{TagPrefix}{configName}:{slabId.Value}:";

    public static int Clean(Document doc, ElementId slabId, string configName)
    {
        string prefix = PrefixFor(configName, slabId);

        var categories = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Rebar,
            BuiltInCategory.OST_AreaRein,
            BuiltInCategory.OST_PathRein,
        };

        var toDelete = new List<ElementId>();
        foreach (BuiltInCategory bic in categories)
        {
            IEnumerable<ElementId> hits = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .Where(e => HasTagPrefix(e, prefix))
                .Select(e => e.Id);
            toDelete.AddRange(hits);
        }

        if (toDelete.Count > 0)
            doc.Delete(toDelete);

        return toDelete.Count;
    }

    private static bool HasTagPrefix(Element e, string prefix)
    {
        string? value = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
        return !string.IsNullOrEmpty(value) && value!.StartsWith(prefix, StringComparison.Ordinal);
    }

    public static void Tag(Element e, string tag)
    {
        Parameter? comments = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments is { IsReadOnly: false })
            comments.Set(tag);
    }
}
