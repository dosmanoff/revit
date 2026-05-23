using System.Text.Json.Serialization;

namespace RevitPlugin.Domain.Configs;

public sealed record Units
{
    [JsonPropertyName("length")] public string Length { get; init; } = "mm";
    [JsonPropertyName("rebar_diameter")] public string RebarDiameter { get; init; } = "mm";
    [JsonPropertyName("area")] public string Area { get; init; } = "mm2";
}
