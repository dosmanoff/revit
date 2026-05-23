using System.Text.Json.Serialization;

namespace RevitPlugin.Domain.Configs;

/// <summary>
/// Корневой объект конфигурации армирования (<c>.wrsconfig.json</c>).
/// На текущем этапе (M1) присутствуют только секции, нужные для MVP-сеток.
/// Остальные секции (perimeter, opening, l_corner, …) появятся в M2–M4 и десериализуются как
/// нетипизированные <see cref="System.Text.Json.JsonElement"/>, чтобы не терять данные при round-trip.
/// </summary>
public sealed record RebarConfig
{
    /// <summary>Текущая версия схемы файла.</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonPropertyName("id")] public string Id { get; init; } = Guid.NewGuid().ToString();
    [JsonPropertyName("name")] public string Name { get; init; } = "Untitled";
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("author")] public string? Author { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    [JsonPropertyName("version")] public int Version { get; init; } = 1;

    [JsonPropertyName("units")] public Units Units { get; init; } = new();
    [JsonPropertyName("applicability")] public Applicability Applicability { get; init; } = new();
    [JsonPropertyName("common")] public CommonConfig Common { get; init; } = new();

    [JsonPropertyName("external_reinforcement")] public MeshConfig? ExternalReinforcement { get; init; }
    [JsonPropertyName("internal_reinforcement")] public MeshConfig? InternalReinforcement { get; init; }

    [JsonPropertyName("parameter_mapping")] public ParameterMapping ParameterMapping { get; init; } = new();

    /// <summary>
    /// Сырая JSON-нагрузка для секций, ещё не покрытых типизированными моделями
    /// (perimeter, opening, l_corner, t_connection, additional_edge, additional_face,
    /// dowels, views, schedules, rebar_types, hooks). Сохраняется при сериализации.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement> ExtensionData { get; init; } = new();
}
