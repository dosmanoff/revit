using System.Text.Json.Serialization;

namespace RevitPlugin.Domain.Configs;

/// <summary>
/// Маппинг параметров: что копировать из стены в стержень, что брать из правила,
/// какие константы записывать. См. <c>docs/MODULES.md §M3.1</c>.
/// </summary>
public sealed record ParameterMapping
{
    /// <summary>Имя параметра стены → имя параметра арматуры.</summary>
    [JsonPropertyName("from_wall")] public IReadOnlyDictionary<string, string> FromWall { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Имя поля правила (<c>Role</c>, <c>Position</c>) → имя параметра арматуры.</summary>
    [JsonPropertyName("from_rule")] public IReadOnlyDictionary<string, string> FromRule { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Константные значения (поддерживаются подстановки <c>{config.id}</c>, <c>{job.timestamp}</c>).</summary>
    [JsonPropertyName("constants")] public IReadOnlyDictionary<string, string> Constants { get; init; }
        = new Dictionary<string, string>();
}
