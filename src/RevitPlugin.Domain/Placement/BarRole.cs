namespace RevitPlugin.Domain.Placement;

/// <summary>
/// Роль стержня — пишется в параметр <c>WRS_Bar_Role</c>.
/// Используется для фильтров на видах и группировок в спецификациях.
/// </summary>
public enum BarRole
{
    Vertical,
    Horizontal,
    Edge,
    Diagonal,
    Stirrup,
    Dowel,
    Corner,
    Opening,
    Custom
}
