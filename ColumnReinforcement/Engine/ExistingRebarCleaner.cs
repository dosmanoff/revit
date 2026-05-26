using Autodesk.Revit.DB;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Tags rebar created by this plugin via the <c>Comments</c> parameter as
/// <c>CR:{ConfigName}:{ColumnId}</c>, and removes prior bars carrying the same
/// tag so a re-run is idempotent.
///
/// PR-04 uses <see cref="Tag"/> and <see cref="MakeTag"/> only; <see cref="Clean"/>
/// is wired into the orchestrator in PR-06.
/// </summary>
public static class ExistingRebarCleaner
{
    public const string TagPrefix = "CR:";

    public static string MakeTag(string configName, ElementId columnId) =>
        $"{TagPrefix}{configName}:{columnId.Value}";

    public static int Clean(Document doc, ElementId columnId, string configName)
    {
        string tag = MakeTag(configName, columnId);

        var toDelete = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rebar)
            .WhereElementIsNotElementType()
            .Where(e => HasTag(e, tag))
            .Select(e => e.Id)
            .ToList();

        if (toDelete.Count > 0)
            doc.Delete(toDelete);

        return toDelete.Count;
    }

    public static void Tag(Element e, string tag)
    {
        Parameter? comments = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments is { IsReadOnly: false })
            comments.Set(tag);
    }

    private static bool HasTag(Element e, string tag)
    {
        Parameter? comments = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments is null) return false;
        string? value = comments.AsString();
        return !string.IsNullOrEmpty(value) && value == tag;
    }
}
