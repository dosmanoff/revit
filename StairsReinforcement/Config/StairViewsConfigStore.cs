using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StairsReinforcement.Config;

/// <summary>Persists <see cref="StairViewsConfig"/> as JSON in the document's ExtensibleStorage.</summary>
public static class StairViewsConfigStore
{
    // Fresh GUID — distinct from FolderStorage and every other plugin's schema.
    private static readonly Guid SchemaGuid = new("72A23A86-834D-4C5F-BA09-652784F61E3D");
    private const string SchemaName = "StairViewsConfig";
    private const string FieldConfigJson = "ConfigJson";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static StairViewsConfig Load(Document doc)
    {
        string? json = Read(doc);
        if (json is null) return StairViewsConfig.Default();
        try { return JsonSerializer.Deserialize<StairViewsConfig>(json, JsonOptions) ?? StairViewsConfig.Default(); }
        catch (JsonException) { return StairViewsConfig.Default(); }
    }

    public static void Save(Document doc, StairViewsConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);
        Schema schema = GetOrCreateSchema();

        DataStorage? storage = new FilteredElementCollector(doc)
            .OfClass(typeof(DataStorage)).Cast<DataStorage>()
            .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());

        using var tx = new Transaction(doc, "Stair Views — Save Config");
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
