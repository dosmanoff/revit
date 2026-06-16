namespace StairsReinforcement.Domain;

public enum StairStatus { Success, Skipped, Failed }

public sealed class StairOutcome
{
    public long StairId { get; set; }
    public string? Mark { get; set; }
    public StairStatus Status { get; set; }
    public string? Reason { get; set; }
    public int Created { get; set; }
    public int Replaced { get; set; }
}

public sealed class RunResult
{
    public bool DryRun { get; set; }
    public List<StairOutcome> Outcomes { get; } = new();

    public int Succeeded => Outcomes.Count(o => o.Status == StairStatus.Success);
    public int Skipped => Outcomes.Count(o => o.Status == StairStatus.Skipped);
    public int Failed => Outcomes.Count(o => o.Status == StairStatus.Failed);
    public int TotalCreated => Outcomes.Sum(o => o.Created);
    public int TotalReplaced => Outcomes.Sum(o => o.Replaced);
}
