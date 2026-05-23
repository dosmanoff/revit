using System.Text.Json.Serialization;

namespace RevitPlugin.Domain.Configs;

/// <summary>
/// Условия применимости конфигурации к стене. Используется при выборе
/// конфига в Wall Link и при валидации перед запуском Arm Walls.
/// </summary>
public sealed record Applicability
{
    [JsonPropertyName("wall_kinds")] public IReadOnlyList<string> WallKinds { get; init; } = new[] { "Basic" };
    [JsonPropertyName("wall_mark_pattern")] public string? WallMarkPattern { get; init; }
    [JsonPropertyName("min_thickness")] public double? MinThickness { get; init; }
    [JsonPropertyName("max_thickness")] public double? MaxThickness { get; init; }
}
