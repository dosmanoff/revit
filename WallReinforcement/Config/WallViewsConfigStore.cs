using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace WallReinforcement.Config;

/// <summary>Persists <see cref="WallViewsConfig"/> as JSON in the document's ExtensibleStorage.
/// Mirrors <c>SlabViewsConfigStore</c>.</summary>
public static class WallViewsConfigStore
{
    private static readonly Guid SchemaGuid = new("B5E2A7C8-1D3F-4E6A-9C0B-2F4A6D8E1C30");
    private const string SchemaName = "WallViewsConfig";
    private const string FieldConfigJson = "ConfigJson";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static WallViewsConfig Load(Document doc)
    {
        string? json = Read(doc);
        if (json is null) return WallViewsConfig.Default();
        try { return JsonSerializer.Deserialize<WallViewsConfig>(json, JsonOptions) ?? WallViewsConfig.Default(); }
        catch (JsonException) { return WallViewsConfig.Default(); }
    }

    public static void Save(Document doc, WallViewsConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);
        Schema schema = GetOrCreateSchema();

        DataStorage? storage = new FilteredElementCollector(doc)
            .OfClass(typeof(DataStorage)).Cast<DataStorage>()
            .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());

        using var tx = new Transaction(doc, "Wall Views — Save Config");
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
        if (schema is null) return null;

        DataStorage? storage = new FilteredElementCollector(doc)
            .OfClass(typeof(DataStorage)).Cast<DataStorage>()
            .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());
        if (storage is null) return null;

        return storage.GetEntity(schema).Get<string>(schema.GetField(FieldConfigJson));
    }

    private static Schema GetOrCreateSchema()
    {
        Schema? existing = Schema.Lookup(SchemaGuid);
        if (existing is not null) return existing;

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(SchemaName);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(FieldConfigJson, typeof(string));
        return builder.Finish();
    }
}
