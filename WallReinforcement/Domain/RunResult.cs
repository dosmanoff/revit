using Autodesk.Revit.DB;

namespace WallReinforcement.Domain;

public enum WallStatus { Success, Skipped, Failed }

public class WallOutcome
{
    public ElementId WallId    { get; init; } = ElementId.InvalidElementId;
    public string    WallName  { get; init; } = string.Empty;
    public WallStatus Status   { get; init; }
    public string?   Reason    { get; init; }
    public int       Created   { get; init; }
    public int       Replaced  { get; init; }
}

public class RunResult
{
    public List<WallOutcome> Outcomes { get; } = [];
    public bool DryRun { get; init; }

    public int Succeeded => Outcomes.Count(o => o.Status == WallStatus.Success);
    public int Skipped   => Outcomes.Count(o => o.Status == WallStatus.Skipped);
    public int Failed    => Outcomes.Count(o => o.Status == WallStatus.Failed);
    public int TotalCreated  => Outcomes.Sum(o => o.Created);
    public int TotalReplaced => Outcomes.Sum(o => o.Replaced);
}
