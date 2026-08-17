using RevitActionRecorder.Model;

namespace RevitActionRecorder.Snapshot;

/// <summary>
/// Теневой кэш: ElementId → (значения параметров + подпись положения).
/// Заполняется во время снапшота «до», обновляется на каждом DocumentChanged,
/// отдаёт дельты {param, from, to} и смещение для изменённых элементов.
/// Держит только строки и числа, не POCO — модель может быть на сотни тысяч элементов.
/// </summary>
internal sealed class ParamCache
{
    private readonly Dictionary<long, Dictionary<long, string?>> _params = new();
    private readonly Dictionary<long, LocationSignature> _locations = new();
    private readonly ParamDefRegistry _defs;
    private readonly HashSet<string> _skippedBips;

    public ParamCache(ParamDefRegistry defs, bool skipWorksharingParams)
    {
        _defs = defs;
        // «Edited by» проставляется совместной работой на каждом тронутом элементе и к методике
        // отношения не имеет — в потоке событий это чистый шум.
        _skippedBips = skipWorksharingParams
            ? new HashSet<string>(StringComparer.Ordinal) { "EDITED_BY" }
            : new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>true после успешного снапшота «до»; без него дельты не считаются.</summary>
    public bool Ready { get; set; }

    public int Count => _params.Count;

    public void Put(long elementId, List<ParamValue> values, LocationSignature? location)
    {
        var map = new Dictionary<long, string?>(values.Count);
        foreach (var v in values)
            map[v.Pid] = ParameterExtractor.ValueOf(v);
        _params[elementId] = map;

        if (location is { } loc) _locations[elementId] = loc;
        else _locations.Remove(elementId);
    }

    public void Remove(long elementId)
    {
        _params.Remove(elementId);
        _locations.Remove(elementId);
    }

    /// <summary>Дельты параметров элемента против кэша; кэш обновляется текущими значениями.</summary>
    public List<ParamDelta>? DiffParams(long elementId, List<ParamValue> current)
    {
        if (!_params.TryGetValue(elementId, out var old))
        {
            var fresh = new Dictionary<long, string?>(current.Count);
            foreach (var v in current) fresh[v.Pid] = ParameterExtractor.ValueOf(v);
            _params[elementId] = fresh;
            return null;
        }

        List<ParamDelta>? deltas = null;
        var currentMap = new Dictionary<long, string?>(current.Count);

        foreach (var v in current)
        {
            var value = ParameterExtractor.ValueOf(v);
            currentMap[v.Pid] = value;

            bool had = old.TryGetValue(v.Pid, out var was);
            if ((had && string.Equals(was, value, StringComparison.Ordinal)) || (!had && value is null))
                continue;
            AddDelta(ref deltas, v.Pid, had ? was : null, value);
        }

        foreach (var kv in old)
        {
            if (currentMap.ContainsKey(kv.Key))
                continue;
            AddDelta(ref deltas, kv.Key, kv.Value, null);
        }

        _params[elementId] = currentMap;
        return deltas;
    }

    /// <summary>Дельта положения; кэш обновляется текущей подписью.</summary>
    public LocDelta? DiffLocation(long elementId, LocationSignature? current)
    {
        _locations.TryGetValue(elementId, out var before);
        bool hadBefore = _locations.ContainsKey(elementId);

        if (current is not { } now)
        {
            _locations.Remove(elementId);
            return null;
        }

        _locations[elementId] = now;
        if (!hadBefore || now.SameAs(before))
            return null;

        return new LocDelta
        {
            Kind = now.Kind,
            From = before.Values,
            To = now.Values,
            By = now.DisplacementFrom(before),
        };
    }

    private void AddDelta(ref List<ParamDelta>? deltas, long pid, string? from, string? to)
    {
        string? name = null, bip = null;
        if (_defs.TryGet(pid, out var def))
        {
            if (def.Bip is not null && _skippedBips.Contains(def.Bip))
                return;
            name = def.Name;
            bip = def.Bip;
        }
        (deltas ??= []).Add(new ParamDelta
        {
            Name = name ?? pid.ToString(),
            Bip = bip,
            From = from,
            To = to,
        });
    }
}
