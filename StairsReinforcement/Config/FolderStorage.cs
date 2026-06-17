using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StairsReinforcement.Config;

/// <summary>
/// Persists the user's chosen config folder and CSV path in <see cref="ProjectInfo"/>
/// via ExtensibleStorage, so they travel with the .rvt. Ported from
/// SlabReinforcement.Config.FolderStorage with a fresh schema GUID.
/// </summary>
public static class FolderStorage
{
    // Fresh GUID — must differ from every other plugin's storage schema.
    private static readonly Guid SchemaGuid = new("22816A66-995F-4B3B-916D-73DA88E701A0");
    private const string ConfigFolderField = "ConfigFolder";
    private const string CsvPathField = "CsvPath";

    public static string? GetConfigFolder(Document doc) => GetField(doc, ConfigFolderField);
    public static void SetConfigFolder(Document doc, string value) => SetField(doc, ConfigFolderField, value);

    public static string? GetCsvPath(Document doc) => GetField(doc, CsvPathField);
    public static void SetCsvPath(Document doc, string value) => SetField(doc, CsvPathField, value);

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
        sb.SetSchemaName("StairsReinforcementSettings");
        sb.SetReadAccessLevel(AccessLevel.Public);
        sb.SetWriteAccessLevel(AccessLevel.Public);
        sb.AddSimpleField(ConfigFolderField, typeof(string));
        sb.AddSimpleField(CsvPathField, typeof(string));
        return sb.Finish();
    }
}
