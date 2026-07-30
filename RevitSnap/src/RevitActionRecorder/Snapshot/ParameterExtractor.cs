using System.Globalization;
using Autodesk.Revit.DB;

namespace RevitActionRecorder.Snapshot;

internal sealed record ParamRecord(
    long Pid,
    string Name,
    string? Bip,
    string? Guid,
    string Storage,
    string? DataType,
    string? Raw,
    string? Display,
    bool ReadOnly,
    bool Shared);

/// <summary>Полное извлечение параметров элемента: GetOrderedParameters + Parameters, дедупликация по Id.</summary>
internal static class ParameterExtractor
{
    public static List<ParamRecord> Extract(Element element)
    {
        var result = new List<ParamRecord>();
        var seen = new HashSet<long>();

        try
        {
            foreach (var p in element.GetOrderedParameters())
                Add(p);
        }
        catch
        {
        }

        try
        {
            foreach (Parameter p in element.Parameters)
                Add(p);
        }
        catch
        {
        }

        return result;

        void Add(Parameter? p)
        {
            try
            {
                if (p is null || !seen.Add(p.Id.Value))
                    return;

                string? bip = null;
                if (p.Definition is InternalDefinition idef && idef.BuiltInParameter != BuiltInParameter.INVALID)
                    bip = idef.BuiltInParameter.ToString();

                string? guid = null;
                try { if (p.IsShared) guid = p.GUID.ToString(); } catch { }

                string? dataType = null;
                try { dataType = p.Definition?.GetDataType()?.TypeId; } catch { }

                string? raw = null;
                try
                {
                    raw = p.StorageType switch
                    {
                        StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                        StorageType.Double => p.AsDouble().ToString("R", CultureInfo.InvariantCulture),
                        StorageType.String => p.AsString(),
                        StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
                        _ => null,
                    };
                }
                catch { }

                string? display = null;
                try { display = p.AsValueString(); } catch { }

                result.Add(new ParamRecord(
                    p.Id.Value,
                    p.Definition?.Name ?? p.Id.Value.ToString(CultureInfo.InvariantCulture),
                    bip,
                    guid,
                    p.StorageType.ToString(),
                    string.IsNullOrEmpty(dataType) ? null : dataType,
                    raw,
                    display,
                    p.IsReadOnly,
                    p.IsShared));
            }
            catch
            {
            }
        }
    }
}
