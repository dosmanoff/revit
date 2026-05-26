using System.IO;
using System.Text.Json;

namespace ColumnReinforcement.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static ColumnReinforcementConfig Load(string path)
    {
        string json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<ColumnReinforcementConfig>(json, Options)
            ?? throw new InvalidDataException($"Config at '{path}' deserialised to null.");

        // Fall back to file-stem if the JSON has no name set.
        if (string.IsNullOrWhiteSpace(cfg.Name) || cfg.Name == "unnamed")
            cfg.Name = Path.GetFileNameWithoutExtension(path);

        return cfg;
    }

    public static void Save(ColumnReinforcementConfig cfg, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, Options));
    }

    /// <summary>Enumerate *.json files in <paramref name="folder"/>, sorted by name.</summary>
    public static IReadOnlyList<string> EnumerateConfigFiles(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
