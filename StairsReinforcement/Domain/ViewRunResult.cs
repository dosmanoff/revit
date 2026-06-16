namespace StairsReinforcement.Domain;

public sealed class ViewRunResult
{
    public int ViewsCreated { get; set; }
    public int SchedulesCreated { get; set; }
    public int SheetsCreated { get; set; }
    public List<string> Errors { get; } = [];

    public void Error(string message) => Errors.Add(message);
}
