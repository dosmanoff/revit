using StairsReinforcement.Config;

namespace StairsReinforcement.Domain;

/// <summary>A problem found while parsing one CSV cell (non-fatal — the default survives).</summary>
public record ParseIssue(int Line, string Field, string Message);

/// <summary>Optional geometry validators from a CSV row (warn on mismatch with the live model).</summary>
public sealed class ExpectedGeom
{
    public Length? Waist { get; set; }
    public Length? Width { get; set; }
    public Length? Rise { get; set; }
    public bool Any => Waist is not null || Width is not null || Rise is not null;
}

/// <summary>Parsed per-stair assignments keyed by a normalised Mark.</summary>
public sealed class AssignmentTable
{
    public required IReadOnlyDictionary<string, StairsReinforcementConfig> ByMark { get; init; }
    public required IReadOnlyDictionary<string, ExpectedGeom> ExpectedByMark { get; init; }
    public required IReadOnlyList<ParseIssue> Issues { get; init; }
    public string? SourcePath { get; init; }

    public static string Key(string mark) => mark.Trim().ToLowerInvariant();

    public StairsReinforcementConfig? TryGetConfig(string? mark) =>
        mark is not null && ByMark.TryGetValue(Key(mark), out StairsReinforcementConfig? c) ? c : null;

    public ExpectedGeom? TryGetExpected(string? mark) =>
        mark is not null && ExpectedByMark.TryGetValue(Key(mark), out ExpectedGeom? e) ? e : null;
}
