using RevitPlugin.Domain.Geometry;

namespace RevitPlugin.Domain.Placement;

/// <summary>
/// Намерение разместить один стержень арматуры.
/// Это доменный объект — он ничего не знает о Revit. Адаптер
/// <c>IRebarFactory</c> переводит его в реальный <c>Autodesk.Revit.DB.Structure.Rebar</c>.
/// </summary>
public sealed record RebarPlacement(
    string RuleId,
    BarRole Role,
    string BarTypeName,
    IReadOnlyList<Line3> Segments,
    HookType HookStart = HookType.None,
    HookType HookEnd = HookType.None,
    string? Position = null,
    IReadOnlyDictionary<string, string>? Parameters = null)
{
    /// <summary>Прямой стержень между двумя точками.</summary>
    public static RebarPlacement Straight(
        string ruleId,
        BarRole role,
        string barTypeName,
        Vec3 start,
        Vec3 end,
        HookType hookStart = HookType.None,
        HookType hookEnd = HookType.None) =>
        new(ruleId, role, barTypeName, new[] { new Line3(start, end) }, hookStart, hookEnd);
}
