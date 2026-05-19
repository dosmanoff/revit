namespace SmartViews.Engine;

public sealed class ViewCreationResult
{
    private readonly List<string> _errors = [];

    public int CreatedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public IReadOnlyList<string> Errors => _errors;
    public int ErrorCount => _errors.Count;
    public bool HasErrors => _errors.Count > 0;

    internal void RecordCreated() => CreatedCount++;
    internal void RecordSkipped() => SkippedCount++;
    internal void RecordError(string message) => _errors.Add(message);
}
