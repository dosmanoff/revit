namespace WallReinforcement.Domain;

/// <summary>Tally + error log for a Wall Views documentation run.</summary>
public sealed class ViewRunResult
{
    public int ViewsCreated { get; set; }
    public int SchedulesCreated { get; set; }
    public int SheetsCreated { get; set; }
    public List<string> Errors { get; } = new();

    public void Error(string message) => Errors.Add(message);
}
