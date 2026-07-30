using Autodesk.Revit.DB;

namespace RevitActionRecorder.Snapshot;

/// <summary>
/// Кэш справочных данных документа на время снапшота: типоразмеры, уровни, стадии, воркcеты.
/// На 144k элементов эти запросы повторялись бы десятки тысяч раз ради одних и тех же имён.
/// Живёт в главном потоке вместе с обходом.
/// </summary>
internal sealed class ElementContext
{
    private readonly Document _doc;
    private readonly bool _workshared;
    private readonly WorksetTable? _worksets;
    private readonly Dictionary<long, (string Family, string Type)?> _types = new();
    private readonly Dictionary<long, string?> _levels = new();
    private readonly Dictionary<long, string?> _phases = new();
    private readonly Dictionary<int, string?> _worksetNames = new();

    public ElementContext(Document doc)
    {
        _doc = doc;
        try { _workshared = doc.IsWorkshared; } catch { }
        if (_workshared)
        {
            try { _worksets = doc.GetWorksetTable(); } catch { }
        }
    }

    public (string Family, string Type)? TypeNames(ElementId typeId)
    {
        var key = typeId.Value;
        if (_types.TryGetValue(key, out var cached))
            return cached;

        (string, string)? value = null;
        try
        {
            if (_doc.GetElement(typeId) is ElementType et)
                value = (et.FamilyName, et.Name);
        }
        catch
        {
        }
        _types[key] = value;
        return value;
    }

    public string? LevelName(ElementId levelId) => Named(_levels, levelId);

    public string? PhaseName(ElementId phaseId) => Named(_phases, phaseId);

    public string? WorksetName(WorksetId worksetId)
    {
        if (!_workshared || _worksets is null || worksetId == WorksetId.InvalidWorksetId)
            return null;

        var key = worksetId.IntegerValue;
        if (_worksetNames.TryGetValue(key, out var cached))
            return cached;

        string? name = null;
        try { name = _worksets.GetWorkset(worksetId)?.Name; } catch { }
        _worksetNames[key] = name;
        return name;
    }

    private string? Named(Dictionary<long, string?> cache, ElementId id)
    {
        var key = id.Value;
        if (cache.TryGetValue(key, out var cached))
            return cached;

        string? name = null;
        try { name = _doc.GetElement(id)?.Name; } catch { }
        cache[key] = name;
        return name;
    }
}
