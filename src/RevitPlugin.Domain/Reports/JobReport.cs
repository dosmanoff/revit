namespace RevitPlugin.Domain.Reports;

/// <summary>
/// Сводный отчёт по запуску оркестратора армирования.
/// Используется в Report Dialog (см. <c>docs/UI_FLOWS.md §2.2</c>).
/// </summary>
public sealed record JobReport(
    string JobId,
    DateTimeOffset Timestamp,
    IReadOnlyList<RuleResult> Results,
    IReadOnlyList<string> Errors)
{
    public int Created => Results.Sum(r => r.Count);
    public int Warnings => Results.Sum(r => r.Warnings.Count);
    public bool HasErrors => Errors.Count > 0;

    public static JobReport Empty(string jobId) =>
        new(jobId, DateTimeOffset.UtcNow, Array.Empty<RuleResult>(), Array.Empty<string>());
}
