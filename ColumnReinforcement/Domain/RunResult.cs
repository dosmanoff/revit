using Autodesk.Revit.DB;

namespace ColumnReinforcement.Domain;

public enum ColumnStatus { Success, Skipped, Failed }

public class ColumnOutcome
{
    public required ElementId ColumnId { get; init; }
    public required ColumnStatus Status { get; init; }
    public string? Reason { get; init; }
    public int Created  { get; init; }
    public int Replaced { get; init; }
}

public class RunResult
{
    public List<ColumnOutcome> Outcomes { get; } = [];
    public bool DryRun { get; init; }

    public int Succeeded    => Outcomes.Count(o => o.Status == ColumnStatus.Success);
    public int Skipped      => Outcomes.Count(o => o.Status == ColumnStatus.Skipped);
    public int Failed       => Outcomes.Count(o => o.Status == ColumnStatus.Failed);
    public int TotalCreated  => Outcomes.Sum(o => o.Created);
    public int TotalReplaced => Outcomes.Sum(o => o.Replaced);
}
