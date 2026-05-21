using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System.IO;
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
            ViewConfig cfg = JsonSerializer.Deserialize<ViewConfig>(json, JsonOptions)
                             ?? ViewConfig.Default();
            return Migrate(cfg);
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
    // File-based preset API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns preset names (without the .json extension) found in <paramref name="folderPath"/>.
    /// Returns an empty list when the folder does not exist or is inaccessible.
    /// </summary>
    public static IReadOnlyList<string> ListPresets(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return [];

        return Directory
            .EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Loads a named preset from <paramref name="folderPath"/>.
    /// Returns null when the file is missing or cannot be parsed.
    /// </summary>
    public static ViewConfig? LoadPreset(string folderPath, string presetName)
    {
        string path = PresetPath(folderPath, presetName);
        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            ViewConfig? cfg = JsonSerializer.Deserialize<ViewConfig>(json, JsonOptions);
            return cfg is null ? null : Migrate(cfg);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Schema migration
    // -----------------------------------------------------------------------

    /// <summary>
    /// Upgrades <paramref name="config"/> to the current schema version in-place.
    /// v0 → v1: converts the uniform "cropOffset" field to the six-sided Offsets object.
    /// </summary>
    private static ViewConfig Migrate(ViewConfig config)
    {
        if (config.SchemaVersion >= ViewConfig.CurrentSchemaVersion)
            return config;

        // v0 → v1
        if (config.SchemaVersion == 0)
        {
            double legacy = config.LegacyCropOffset > 0 ? config.LegacyCropOffset : 1.0;
            config.Offsets = CropOffsets.Uniform(legacy);
            config.SchemaVersion = 1;
        }

        return config;
    }

    /// <summary>
    /// Saves <paramref name="config"/> as a named preset JSON file.
    /// Creates the folder if it does not yet exist.
    /// </summary>
    public static void SavePreset(string folderPath, string presetName, ViewConfig config)
    {
        Directory.CreateDirectory(folderPath);
        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(PresetPath(folderPath, presetName), json);
    }

    private static string PresetPath(string folderPath, string presetName) =>
        Path.Combine(folderPath, $"{presetName}.json");

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
