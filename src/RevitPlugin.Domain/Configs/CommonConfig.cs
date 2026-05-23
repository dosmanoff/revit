using System.Text.Json.Serialization;

namespace RevitPlugin.Domain.Configs;

/// <summary>
/// Общие настройки конфигурации (применяются ко всем правилам, если не переопределены локально).
/// Соответствует секции <c>common</c> в <c>.wrsconfig.json</c>.
/// </summary>
public sealed record CommonConfig
{
    /// <summary>Целевой слой многослойной стены (1-based).</summary>
    [JsonPropertyName("target_layer_index")] public int? TargetLayerIndex { get; init; }

    /// <summary>Глобальный защитный слой по умолчанию, мм.</summary>
    [JsonPropertyName("cover")] public double Cover { get; init; } = 30;

    /// <summary>Значение параметра <c>Partition</c> для группировки стержней при нумерации.</summary>
    [JsonPropertyName("partition")] public string? Partition { get; init; }

    /// <summary>Включать ли в Rebar Set'ы (true) или создавать отдельные стержни (false).</summary>
    [JsonPropertyName("use_rebar_sets")] public bool UseRebarSets { get; init; } = true;

    /// <summary>Исключать ли вставки (двери, окна) при расчёте сеток.</summary>
    [JsonPropertyName("exclude_inserts")] public bool ExcludeInserts { get; init; } = true;
}
