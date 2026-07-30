using RevitActionRecorder.Model;

namespace RevitActionRecorder.Snapshot;

/// <summary>
/// Теневой кэш параметров: ElementId → (paramId → строковое значение).
/// Заполняется во время снапшота «до», обновляется на каждом DocumentChanged,
/// отдаёт дельты {param, from, to} для изменённых элементов.
/// Держит только строки, не POCO — модель может быть на сотни тысяч элементов.
/// </summary>
internal sealed class ParamCache
{
    private readonly Dictionary<long, Dictionary<long, string?>> _elements = new();
    private readonly Dictionary<long, (string Name, string? Bip)> _defs = new();

    /// <summary>true после успешного снапшота «до»; без него дельты не считаются.</summary>
    public bool Ready { get; set; }

    public int Count => _elements.Count;

    public void Put(long elementId, List<ParamRecord> records)
    {
        var map = new Dictionary<long, string?>(records.Count);
        foreach (var r in records)
        {
            map[r.Pid] = r.Display ?? r.Raw;
            _defs.TryAdd(r.Pid, (r.Name, r.Bip));
        }
        _elements[elementId] = map;
    }

    public void Remove(long elementId) => _elements.Remove(elementId);

    /// <summary>Дельты параметров элемента против кэша; кэш обновляется текущими значениями.</summary>
    public List<ParamDelta>? DiffAndUpdate(long elementId, List<ParamRecord> current)
    {
        if (!_elements.TryGetValue(elementId, out var old))
        {
            Put(elementId, current);
            return null;
        }

        List<ParamDelta>? deltas = null;
        var currentMap = new Dictionary<long, string?>(current.Count);

        foreach (var r in current)
        {
            var value = r.Display ?? r.Raw;
            currentMap[r.Pid] = value;
            _defs.TryAdd(r.Pid, (r.Name, r.Bip));

            bool had = old.TryGetValue(r.Pid, out var was);
            if ((had && !string.Equals(was, value, StringComparison.Ordinal)) || (!had && value is not null))
                (deltas ??= []).Add(new ParamDelta { Name = r.Name, Bip = r.Bip, From = had ? was : null, To = value });
        }

        foreach (var kv in old)
        {
            if (currentMap.ContainsKey(kv.Key))
                continue;
            _defs.TryGetValue(kv.Key, out var def);
            (deltas ??= []).Add(new ParamDelta
            {
                Name = def.Name ?? kv.Key.ToString(),
                Bip = def.Bip,
                From = kv.Value,
                To = null,
            });
        }

        _elements[elementId] = currentMap;
        return deltas;
    }
}
