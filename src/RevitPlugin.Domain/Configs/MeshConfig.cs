using System.Text.Json.Serialization;

namespace RevitPlugin.Domain.Configs;

/// <summary>
/// Конфигурация одной сетки (внешней или внутренней) — вертикальные + горизонтальные стержни.
/// Соответствует секциям <c>external_reinforcement</c> / <c>internal_reinforcement</c>
/// в <c>.wrsconfig.json</c>. См. <c>docs/MODULES.md §M1.1</c>.
/// </summary>
public sealed record MeshConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;

    [JsonPropertyName("bar_type_vertical")] public string BarTypeVertical { get; init; } = "Ø12 A500C";
    [JsonPropertyName("bar_type_horizontal")] public string BarTypeHorizontal { get; init; } = "Ø10 A500C";

    [JsonPropertyName("spacing_vertical")] public double SpacingVertical { get; init; } = 200;
    [JsonPropertyName("spacing_horizontal")] public double SpacingHorizontal { get; init; } = 200;

    /// <summary>Защитный слой от целевой грани слоя стены, мм.</summary>
    [JsonPropertyName("cover")] public double Cover { get; init; } = 30;

    /// <summary>Какая сетка ближе к грани — <c>Vertical</c> или <c>Horizontal</c>.</summary>
    [JsonPropertyName("major")] public string Major { get; init; } = "Vertical";

    /// <summary>
    /// На сколько вертикальный стержень не доходит до верха стены, мм.
    /// Также определяет верхнюю границу зоны размещения горизонтальных стержней.
    /// </summary>
    [JsonPropertyName("vertical_offset_top")] public double VerticalOffsetTop { get; init; } = 200;

    /// <summary>
    /// На сколько вертикальный стержень не доходит до низа стены, мм.
    /// Также определяет нижнюю границу зоны размещения горизонтальных стержней.
    /// </summary>
    [JsonPropertyName("vertical_offset_bottom")] public double VerticalOffsetBottom { get; init; } = 200;

    /// <summary>Отступ горизонтального стержня от начала стены, мм.</summary>
    [JsonPropertyName("horizontal_offset_start")] public double HorizontalOffsetStart { get; init; } = 100;

    /// <summary>Отступ горизонтального стержня от конца стены, мм.</summary>
    [JsonPropertyName("horizontal_offset_end")] public double HorizontalOffsetEnd { get; init; } = 100;

    /// <summary>Дистанция отступа вертикальных стержней от торца стены, мм.</summary>
    [JsonPropertyName("wall_end_offset_distance")] public double WallEndOffsetDistance { get; init; } = 100;

    /// <summary>Режим отступа от торцов: <c>FromStart</c>, <c>FromEnd</c>, <c>FromStartEnd</c>, <c>Centered</c>.</summary>
    [JsonPropertyName("wall_end_offset_mode")] public string WallEndOffsetMode { get; init; } = "FromStartEnd";

    [JsonPropertyName("hook_start")] public string HookStart { get; init; } = "None";
    [JsonPropertyName("hook_end")] public string HookEnd { get; init; } = "None";
}
