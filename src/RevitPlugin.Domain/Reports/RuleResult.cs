using RevitPlugin.Domain.Placement;

namespace RevitPlugin.Domain.Reports;

/// <summary>Итог работы одного правила на одной стене.</summary>
public sealed record RuleResult(
    string RuleId,
    long WallId,
    IReadOnlyList<RebarPlacement> Placements,
    IReadOnlyList<string> Warnings)
{
    public bool HasWarnings => Warnings.Count > 0;
    public int Count => Placements.Count;
}
