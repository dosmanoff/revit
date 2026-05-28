using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System.Text.Json;

namespace SmartViews.Config;

/// <summary>
/// Persists <see cref="ColumnViewsConfig"/> as JSON in the document's ExtensibleStorage,
/// independent of the SmartViews <see cref="ConfigLoader"/> schema.
/// </summary>
public static class ColumnViewsConfigStore
{
    private static readonly Guid SchemaGuid = new("C7E1A2B3-4D5E-6F70-8192-A3B4C5D6E7F8");
    private const string SchemaName = "ColumnViewsConfig";
    private const string FieldConfigJson = "ConfigJson";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static ColumnViewsConfig Load(Document doc)
    {
        string? json = Read(doc);
        if (json is null)
            return ColumnViewsConfig.Default();

        try
        {
            return JsonSerializer.Deserialize<ColumnViewsConfig>(json, JsonOptions)
                   ?? ColumnViewsConfig.Default();
        }
        catch (JsonException)
        {
            return ColumnViewsConfig.Default();
        }
    }

    public static void Save(Document doc, ColumnViewsConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);

        Schema schema = GetOrCreateSchema();

        using var collector = new FilteredElementCollector(doc);
        DataStorage? storage = collector
            .OfClass(typeof(DataStorage))
            .Cast<DataStorage>()
            .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());

        using var tx = new Transaction(doc, "Column Views — Save Config");
        tx.Start();

        storage ??= DataStorage.Create(doc);

        var entity = new Entity(schema);
        entity.Set(schema.GetField(FieldConfigJson), json);
        storage.SetEntity(entity);

        tx.Commit();
    }

    private static string? Read(Document doc)
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
            return null;

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
