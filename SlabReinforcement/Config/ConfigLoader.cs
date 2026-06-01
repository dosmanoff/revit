using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlabReinforcement.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SlabReinforcementConfig Load(string path)
    {
        string json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<SlabReinforcementConfig>(json, Options)
            ?? throw new InvalidDataException($"Config at '{path}' deserialised to null.");

        if (string.IsNullOrWhiteSpace(cfg.Name) || cfg.Name == "default")
            cfg.Name = Path.GetFileNameWithoutExtension(path);

        return cfg;
    }

    public static void Save(SlabReinforcementConfig cfg, string path)
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
