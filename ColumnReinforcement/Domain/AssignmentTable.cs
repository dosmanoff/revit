using ColumnReinforcement.Config;

namespace ColumnReinforcement.Domain;

/// <summary>
/// In-memory result of parsing an assignments CSV — one
/// <see cref="ColumnReinforcementConfig"/> per <c>Mark</c>, plus the
/// expected-geometry metadata used by the dialog's validation table.
/// Lookups are case-insensitive on Mark.
/// </summary>
public class AssignmentTable
{
    private readonly Dictionary<string, ColumnReinforcementConfig> _byMark;
    private readonly Dictionary<string, ExpectedGeometry?> _expectedByMark;

    public AssignmentTable(
        Dictionary<string, ColumnReinforcementConfig> byMark,
        Dictionary<string, ExpectedGeometry?> expectedByMark,
        IReadOnlyList<ParseIssue> issues,
        string? sourcePath = null)
    {
        _byMark         = byMark;
        _expectedByMark = expectedByMark;
        Issues          = issues;
        SourcePath      = sourcePath;
    }

    public IReadOnlyDictionary<string, ColumnReinforcementConfig> ByMark         => _byMark;
    public IReadOnlyDictionary<string, ExpectedGeometry?>         ExpectedByMark => _expectedByMark;
    public IReadOnlyList<ParseIssue>                              Issues         { get; }
    public string?                                                SourcePath     { get; }

    public ColumnReinforcementConfig? TryGetConfig(string? mark) =>
        !string.IsNullOrWhiteSpace(mark) && _byMark.TryGetValue(mark!, out var cfg) ? cfg : null;

    public ExpectedGeometry? TryGetExpected(string? mark) =>
        !string.IsNullOrWhiteSpace(mark) && _expectedByMark.TryGetValue(mark!, out var e) ? e : null;
}

/// <summary>
/// Per-row expected geometry for validation only. Engine always uses the real
/// Revit column geometry; the dialog displays this side-by-side with the actual
/// dimensions and flags mismatches as warnings.
/// </summary>
public record ExpectedGeometry(ColumnSection Section, double? WidthIn, double? DepthIn, double? DiameterIn);

/// <summary>
/// A row-level CSV parsing problem. <see cref="LineNumber"/> is 1-based and
/// refers to the source file as the user would see it in an editor.
/// </summary>
public record ParseIssue(int LineNumber, string Field, string Message);
