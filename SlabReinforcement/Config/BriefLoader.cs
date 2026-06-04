using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlabReinforcement.Config;

/// <summary>Loads/saves the structured JSON reinforcement brief (<see cref="SlabBrief"/>).</summary>
public static class BriefLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SlabBrief Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SlabBrief>(json, Options)
               ?? throw new InvalidDataException($"Brief at '{path}' deserialised to null.");
    }

    public static SlabBrief Parse(string json) =>
        JsonSerializer.Deserialize<SlabBrief>(json, Options)
        ?? throw new InvalidDataException("Brief JSON deserialised to null.");

    public static void Save(SlabBrief brief, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(brief, Options));
    }

    /// <summary>Find the brief entry for a floor by Mark (case-insensitive) or element id.</summary>
    public static BriefSlab? Match(SlabBrief brief, string? mark, long elementId)
    {
        foreach (BriefSlab s in brief.Slabs)
        {
            if (s.ElementId != 0 && s.ElementId == elementId) return s;
            if (!string.IsNullOrWhiteSpace(s.Mark) && !string.IsNullOrWhiteSpace(mark)
                && string.Equals(s.Mark, mark, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return null;
    }
}
