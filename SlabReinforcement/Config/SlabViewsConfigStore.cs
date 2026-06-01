using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace SlabReinforcement.Config;

/// <summary>Persists <see cref="SlabViewsConfig"/> as JSON in the document's ExtensibleStorage.</summary>
public static class SlabViewsConfigStore
{
    private static readonly Guid SchemaGuid = new("724BAE5B-991E-443F-9AEF-345D2CB759DB");
    private const string SchemaName = "SlabViewsConfig";
    private const string FieldConfigJson = "ConfigJson";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SlabViewsConfig Load(Document doc)
    {
        string? json = Read(doc);
        if (json is null) return SlabViewsConfig.Default();
        try { return JsonSerializer.Deserialize<SlabViewsConfig>(json, JsonOptions) ?? SlabViewsConfig.Default(); }
        catch (JsonException) { return SlabViewsConfig.Default(); }
    }

    public static void Save(Document doc, SlabViewsConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);
        Schema schema = GetOrCreateSchema();

        DataStorage? storage = new FilteredElementCollector(doc)
            .OfClass(typeof(DataStorage)).Cast<DataStorage>()
            .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());

        using var tx = new Transaction(doc, "Slab Views — Save Config");
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
