using System.Text.Json.Serialization;

namespace RevitPlugin.Domain.Configs;

/// <summary>
/// Окантовка проёма прямыми стержнями по сторонам (без диагональных и U/O-хомутов).
/// Соответствует <c>opening.edge_rebar</c> в <c>.wrsconfig.json</c>.
/// См. <c>docs/MODULES.md §M1.3</c>.
/// </summary>
public sealed record OpeningEdgeConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;

    [JsonPropertyName("bar_type")] public string BarType { get; init; } = "Ø12 A500C";

    /// <summary>Сколько стержней на сторону.</summary>
    [JsonPropertyName("count")] public int Count { get; init; } = 1;

    /// <summary>Минимальная ширина проёма, при которой правило срабатывает, мм.</summary>
    [JsonPropertyName("min_width")] public double MinWidth { get; init; } = 0;

    /// <summary>Максимальная ширина проёма, при которой правило срабатывает, мм. <c>null</c> — без ограничения.</summary>
    [JsonPropertyName("max_width")] public double? MaxWidth { get; init; }

    /// <summary>Какие стороны окантовывать: <c>Top</c>, <c>Bottom</c>, <c>Left</c>, <c>Right</c>, <c>All</c>.</summary>
    [JsonPropertyName("sides")] public string Sides { get; init; } = "All";

    /// <summary>Длина анкеровки за угол проёма (выпуск стержня за бровку), мм.</summary>
    [JsonPropertyName("anchorage_length")] public double AnchorageLength { get; init; } = 400;

    /// <summary>Отступ от грани проёма (вдоль ребра проёма), мм.</summary>
    [JsonPropertyName("edge_cover")] public double EdgeCover { get; init; } = 30;

    /// <summary>На какой грани стены: <c>External</c>, <c>Internal</c>, <c>Both</c>.</summary>
    [JsonPropertyName("position")] public string Position { get; init; } = "Both";
}

public sealed record OpeningConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
    [JsonPropertyName("edge_rebar")] public OpeningEdgeConfig EdgeRebar { get; init; } = new();
}
