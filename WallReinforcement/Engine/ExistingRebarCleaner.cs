using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace WallReinforcement.Engine;

/// <summary>
/// Finds rebar previously placed by this plugin on a wall (tagged via the
/// <c>Comments</c> parameter as <c>WR:{ConfigName}:{WallId}</c>) and deletes it.
/// Lets a re-run be idempotent: the wall ends up with only the latest config's rebar.
/// </summary>
public static class ExistingRebarCleaner
{
    public const string TagPrefix = "WR:";

    public static string MakeTag(string configName, ElementId wallId) =>
        $"{TagPrefix}{configName}:{wallId.Value}";

    public static int Clean(Document doc, ElementId wallId, string configName)
    {
        string tag = MakeTag(configName, wallId);

        var categories = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Rebar,
            BuiltInCategory.OST_AreaRein,
            BuiltInCategory.OST_PathRein,
        };

        var toDelete = new List<ElementId>();
        foreach (var bic in categories)
        {
            var collected = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .Where(e => HasTag(e, tag))
                .Select(e => e.Id);
            toDelete.AddRange(collected);
        }

        if (toDelete.Count > 0)
            doc.Delete(toDelete);

        return toDelete.Count;
    }

    private static bool HasTag(Element e, string tag)
    {
        Parameter? comments = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments is null) return false;
        string? value = comments.AsString();
        return !string.IsNullOrEmpty(value) && value == tag;
    }

    public static void Tag(Element e, string tag)
    {
        Parameter? comments = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments is { IsReadOnly: false })
            comments.Set(tag);
    }
}
