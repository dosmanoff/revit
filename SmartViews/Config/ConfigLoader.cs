using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System.Text.Json;

namespace SmartViews.Config;

/// <summary>
/// Serialises/deserialises ViewConfig as JSON.
/// The config folder path is persisted in Revit ExtensibleStorage so it
/// survives project saves and re-opens without modifying any project elements.
/// </summary>
public static class ConfigLoader
{
    // ---------- ExtensibleStorage schema identifiers ----------
    private static readonly Guid SchemaGuid = new("B3C4D5E6-F7A8-9012-BCDE-F01234567890");
    private const string SchemaName = "SmartViewsConfig";
    private const string FieldConfigJson = "ConfigJson";

    // ---------- JSON options ----------
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // -----------------------------------------------------------------------

    public static ViewConfig Load(Document doc)
    {
        string? json = ReadFromExtensibleStorage(doc);
        if (json is null)
            return ViewConfig.Default();

        try
        {
            return JsonSerializer.Deserialize<ViewConfig>(json, JsonOptions)
                   ?? ViewConfig.Default();
        }
        catch (JsonException)
        {
            // Corrupt or outdated stored config — fall back to defaults.
            return ViewConfig.Default();
        }
    }

    public static void Save(Document doc, ViewConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);
        WriteToExtensibleStorage(doc, json);
    }

    // -----------------------------------------------------------------------
    // ExtensibleStorage helpers
    // -----------------------------------------------------------------------

    private static string? ReadFromExtensibleStorage(Document doc)
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
            return null;

        // DataStorage elements are project-level singleton containers.
        using var collector = new FilteredElementCollector(doc);
        DataStorage? storage = collector
            .OfClass(typeof(DataStorage))
            .Cast<DataStorage>()
            .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());

        if (storage is null)
            return null;

        Entity entity = storage.GetEntity(schema);
        return entity.Get<string>(schema.GetField(FieldConfigJson));
    }

    private static void WriteToExtensibleStorage(Document doc, string json)
    {
        Schema schema = GetOrCreateSchema();

        using var collector = new FilteredElementCollector(doc);
        DataStorage? storage = collector
            .OfClass(typeof(DataStorage))
            .Cast<DataStorage>()
            .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());

        using var tx = new Transaction(doc, "SmartViews — Save Config");
        tx.Start();

        if (storage is null)
            storage = DataStorage.Create(doc);

        var entity = new Entity(schema);
        entity.Set(schema.GetField(FieldConfigJson), json);
        storage.SetEntity(entity);

        tx.Commit();
    }

    private static Schema GetOrCreateSchema()
    {
        Schema? existing = Schema.Lookup(SchemaGuid);
        if (existing is not null)
            return existing;

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(SchemaName);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(FieldConfigJson, typeof(string));

        return builder.Finish();
    }
}
