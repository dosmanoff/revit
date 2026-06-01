using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace SlabReinforcement.Config;

/// <summary>
/// Persists the user's chosen config folder and CSV paths in <see cref="ProjectInfo"/>
/// via ExtensibleStorage, so they travel with the .rvt. Three string fields:
/// the config folder, the assignments CSV, and the zones CSV.
/// </summary>
public static class FolderStorage
{
    private static readonly Guid SchemaGuid = new("65E2BEDE-C9AA-4D48-A524-B5E25B110F33");
    private const string ConfigFolderField = "ConfigFolder";
    private const string CsvPathField = "CsvPath";
    private const string ZonesPathField = "ZonesPath";

    public static string? GetConfigFolder(Document doc) => GetField(doc, ConfigFolderField);
    public static void SetConfigFolder(Document doc, string value) => SetField(doc, ConfigFolderField, value);

    public static string? GetCsvPath(Document doc) => GetField(doc, CsvPathField);
    public static void SetCsvPath(Document doc, string value) => SetField(doc, CsvPathField, value);

    public static string? GetZonesPath(Document doc) => GetField(doc, ZonesPathField);
    public static void SetZonesPath(Document doc, string value) => SetField(doc, ZonesPathField, value);

    private static string? GetField(Document doc, string field)
    {
        Schema schema = Schema.Lookup(SchemaGuid) ?? CreateSchema();
        Entity entity = doc.ProjectInformation.GetEntity(schema);
        if (entity is null || !entity.IsValid()) return null;

        try
        {
            string value = entity.Get<string>(field);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch { return null; }
    }

    private static void SetField(Document doc, string field, string value)
    {
        Schema schema = Schema.Lookup(SchemaGuid) ?? CreateSchema();

        // Preserve the other fields when updating one.
        Entity entity = doc.ProjectInformation.GetEntity(schema);
        if (entity is null || !entity.IsValid()) entity = new Entity(schema);

        entity.Set(ConfigFolderField, ReadOr(entity, ConfigFolderField, field, value));
        entity.Set(CsvPathField, ReadOr(entity, CsvPathField, field, value));
        entity.Set(ZonesPathField, ReadOr(entity, ZonesPathField, field, value));

        doc.ProjectInformation.SetEntity(entity);
    }

    private static string ReadOr(Entity entity, string thisField, string updatingField, string newValue)
    {
        if (thisField == updatingField) return newValue ?? string.Empty;
        try { return entity.Get<string>(thisField) ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static Schema CreateSchema()
    {
        var sb = new SchemaBuilder(SchemaGuid);
        sb.SetSchemaName("SlabReinforcementSettings");
        sb.SetReadAccessLevel(AccessLevel.Public);
        sb.SetWriteAccessLevel(AccessLevel.Public);
        sb.AddSimpleField(ConfigFolderField, typeof(string));
        sb.AddSimpleField(CsvPathField, typeof(string));
        sb.AddSimpleField(ZonesPathField, typeof(string));
        return sb.Finish();
    }
}
