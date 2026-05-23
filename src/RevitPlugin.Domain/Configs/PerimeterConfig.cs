using System.Text.Json.Serialization;

namespace RevitPlugin.Domain.Configs;

/// <summary>
/// Краевая арматура по периметру стены (без диагональных и U/O-хомутов — те идут отдельно).
/// Соответствует <c>perimeter.edge_rebar</c> в <c>.wrsconfig.json</c>.
/// См. <c>docs/MODULES.md §M1.2</c>.
/// </summary>
public sealed record PerimeterEdgeConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;

    [JsonPropertyName("bar_type")] public string BarType { get; init; } = "Ø12 A500C";

    /// <summary>Сколько «ниток» — 1 или 2.</summary>
    [JsonPropertyName("count")] public int Count { get; init; } = 1;

    /// <summary>Длина L-загиба в углах стены, мм. 0 — без загиба (прямой стержень).</summary>
    [JsonPropertyName("l_leg_length")] public double LLegLength { get; init; } = 0;

    /// <summary>На какой грани располагать: <c>External</c>, <c>Internal</c>, <c>Both</c>, <c>Center</c>.</summary>
    [JsonPropertyName("position")] public string Position { get; init; } = "Both";

    /// <summary>Отступ от грани края (вдоль ребра), мм.</summary>
    [JsonPropertyName("edge_cover")] public double EdgeCover { get; init; } = 30;

    /// <summary>Отступ от концов стержня, мм.</summary>
    [JsonPropertyName("end_cover")] public double EndCover { get; init; } = 30;

    /// <summary>Какие края обрабатывать: <c>Top</c>, <c>Bottom</c>, <c>Left</c>, <c>Right</c>, <c>All</c>, либо комбинация через запятую.</summary>
    [JsonPropertyName("edges")] public string Edges { get; init; } = "All";
}

public sealed record PerimeterConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
    [JsonPropertyName("edge_rebar")] public PerimeterEdgeConfig EdgeRebar { get; init; } = new();
}
