using System.Text.Json;
using Autodesk.Revit.DB;

namespace RevitActionRecorder.Snapshot;

internal sealed record ParamDef(
    string Name,
    string? Bip,
    string? Guid,
    string Storage,
    string? DataType,
    bool Shared);

/// <summary>
/// Словарь определений параметров на сессию: pid → имя/bip/guid/тип хранения/спецификация.
/// Всё это — свойства ОПРЕДЕЛЕНИЯ, одинаковые для всех элементов, поэтому вычисляются один раз
/// (на модели 144k элементов повторный GetDataType стоил ~18 с) и пишутся в снапшот одной секцией.
/// </summary>
internal sealed class ParamDefRegistry
{
    private readonly Dictionary<long, ParamDef> _defs = new();

    public int Count => _defs.Count;

    public bool TryGet(long pid, out ParamDef def) => _defs.TryGetValue(pid, out def!);

    /// <summary>Определение по параметру; вычисляется только при первой встрече pid.</summary>
    public ParamDef GetOrAdd(Parameter parameter)
    {
        var pid = parameter.Id.Value;
        if (_defs.TryGetValue(pid, out var existing))
            return existing;

        string? bip = null;
        if (parameter.Definition is InternalDefinition idef && idef.BuiltInParameter != BuiltInParameter.INVALID)
            bip = idef.BuiltInParameter.ToString();

        bool shared = false;
        string? guid = null;
        try
        {
            shared = parameter.IsShared;
            if (shared) guid = parameter.GUID.ToString();
        }
        catch
        {
        }

        string? dataType = null;
        try
        {
            var typeId = parameter.Definition?.GetDataType()?.TypeId;
            if (!string.IsNullOrEmpty(typeId)) dataType = typeId;
        }
        catch
        {
        }

        string name;
        try { name = parameter.Definition?.Name ?? pid.ToString(); }
        catch { name = pid.ToString(); }

        var def = new ParamDef(name, bip, guid, parameter.StorageType.ToString(), dataType, shared);
        _defs[pid] = def;
        return def;
    }

    public void Write(Utf8JsonWriter w)
    {
        w.WritePropertyName("paramDefs");
        w.WriteStartObject();
        foreach (var (pid, def) in _defs)
        {
            w.WritePropertyName(pid.ToString());
            w.WriteStartObject();
            w.WriteString("name", def.Name);
            if (def.Bip is not null) w.WriteString("bip", def.Bip);
            if (def.Guid is not null) w.WriteString("guid", def.Guid);
            w.WriteString("storage", def.Storage);
            if (def.DataType is not null) w.WriteString("dataType", def.DataType);
            if (def.Shared) w.WriteBoolean("shared", true);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }
}
