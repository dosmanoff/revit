namespace SlabReinforcement.Domain;

public enum SlabStatus { Success, Skipped, Failed }

public sealed class SlabOutcome
{
    public required long SlabId { get; init; }
    public string? Mark { get; init; }
    public SlabStatus Status { get; set; }
    public string? Reason { get; set; }
    public int Created { get; set; }
    public int Replaced { get; set; }
}

public sealed class RunResult
{
    public bool DryRun { get; init; }
    public List<SlabOutcome> Outcomes { get; } = [];

    public int Succeeded => Outcomes.Count(o => o.Status == SlabStatus.Success);
    public int Skipped => Outcomes.Count(o => o.Status == SlabStatus.Skipped);
    public int Failed => Outcomes.Count(o => o.Status == SlabStatus.Failed);
    public int TotalCreated => Outcomes.Sum(o => o.Created);
    public int TotalReplaced => Outcomes.Sum(o => o.Replaced);
}
