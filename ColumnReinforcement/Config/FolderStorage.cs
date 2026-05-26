using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ColumnReinforcement.Config;

/// <summary>
/// Persists the user's chosen config folder in <see cref="ProjectInfo"/> via ExtensibleStorage,
/// so it travels with the .rvt instead of living in a user-machine setting.
///
/// Distinct schema GUID from <c>WallReinforcement.Config.FolderStorage</c> — each plugin
/// keeps its own folder preference independent of the others.
/// </summary>
public static class FolderStorage
{
    private static readonly Guid SchemaGuid = new("847D1965-6B76-4A6B-A82D-61D0E8948ADF");
    private const string FieldName = "ConfigFolder";

    public static string? Get(Document doc)
    {
        Schema schema = Schema.Lookup(SchemaGuid) ?? CreateSchema();
        Entity entity = doc.ProjectInformation.GetEntity(schema);
        if (entity is null || !entity.IsValid()) return null;

        try { return entity.Get<string>(FieldName); }
        catch { return null; }
    }

    public static void Set(Document doc, string folder)
    {
        Schema schema = Schema.Lookup(SchemaGuid) ?? CreateSchema();
        var entity = new Entity(schema);
        entity.Set(FieldName, folder ?? string.Empty);
        doc.ProjectInformation.SetEntity(entity);
    }

    private static Schema CreateSchema()
    {
        var sb = new SchemaBuilder(SchemaGuid);
        sb.SetSchemaName("ColumnReinforcementSettings");
        sb.SetReadAccessLevel(AccessLevel.Public);
        sb.SetWriteAccessLevel(AccessLevel.Public);
        sb.AddSimpleField(FieldName, typeof(string));
        return sb.Finish();
    }
}
