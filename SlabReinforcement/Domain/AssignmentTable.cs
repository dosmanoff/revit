using SlabReinforcement.Config;

namespace SlabReinforcement.Domain;

/// <summary>A non-fatal problem found while parsing the assignments / zones CSV.</summary>
public record ParseIssue(int Line, string Field, string Message);

/// <summary>
/// Per-slab configs parsed from <c>slab-assignments.csv</c>, keyed by lowercased Mark,
/// plus expected-thickness values (for the validation table) and parse issues.
/// </summary>
public sealed class AssignmentTable
{
    public IReadOnlyDictionary<string, SlabReinforcementConfig> ByMark { get; init; }
        = new Dictionary<string, SlabReinforcementConfig>();
    public IReadOnlyDictionary<string, double> ExpectedThicknessInByMark { get; init; }
        = new Dictionary<string, double>();
    public IReadOnlyList<ParseIssue> Issues { get; init; } = [];
    public string? SourcePath { get; init; }

    public SlabReinforcementConfig? TryGetConfig(string mark) =>
        ByMark.TryGetValue(Key(mark), out SlabReinforcementConfig? c) ? c : null;

    public double? TryGetExpectedThicknessIn(string mark) =>
        ExpectedThicknessInByMark.TryGetValue(Key(mark), out double v) ? v : null;

    public static string Key(string mark) => mark.Trim().ToLowerInvariant();
}
