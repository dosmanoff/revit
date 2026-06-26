using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallReinforcement.Config;

/// <summary>Loads/saves the structured JSON reinforcement brief (<see cref="WallBrief"/>).</summary>
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

    public static WallBrief Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WallBrief>(json, Options)
               ?? throw new InvalidDataException($"Brief at '{path}' deserialised to null.");
    }

    public static WallBrief Parse(string json) =>
        JsonSerializer.Deserialize<WallBrief>(json, Options)
        ?? throw new InvalidDataException("Brief JSON deserialised to null.");

    public static void Save(WallBrief brief, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(brief, Options));
    }

    /// <summary>Find the brief entry for a wall by Mark (case-insensitive) or element id.</summary>
    public static BriefWall? Match(WallBrief brief, string? mark, long elementId)
    {
        foreach (BriefWall w in brief.Walls)
        {
            if (w.ElementId != 0 && w.ElementId == elementId) return w;
            if (!string.IsNullOrWhiteSpace(w.Mark) && !string.IsNullOrWhiteSpace(mark)
                && string.Equals(w.Mark, mark, StringComparison.OrdinalIgnoreCase))
                return w;
        }
        return null;
    }
}
