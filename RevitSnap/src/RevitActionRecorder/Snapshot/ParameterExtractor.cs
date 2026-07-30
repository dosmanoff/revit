using System.Globalization;
using Autodesk.Revit.DB;

namespace RevitActionRecorder.Snapshot;

/// <summary>Значение параметра у конкретного элемента; метаданные лежат в <see cref="ParamDefRegistry"/>.</summary>
internal readonly record struct ParamValue(long Pid, string? Raw, string? Display, bool ReadOnly);

/// <summary>Полное извлечение параметров элемента: GetOrderedParameters + Parameters, дедупликация по Id.</summary>
internal static class ParameterExtractor
{
    public static List<ParamValue> Extract(Element element, ParamDefRegistry registry)
    {
        var result = new List<ParamValue>();
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

                var def = registry.GetOrAdd(p);

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

                // AsValueString — самая дорогая операция обхода (58 с на 144k элементов), а для
                // строковых параметров она возвращает то же самое, что уже лежит в raw.
                string? display = null;
                if (p.StorageType != StorageType.String)
                {
                    try { display = p.AsValueString(); } catch { }
                    if (display == raw) display = null;
                }

                result.Add(new ParamValue(p.Id.Value, raw, display, p.IsReadOnly));
            }
            catch
            {
            }
        }
    }

    /// <summary>Значение для сравнения в теневом кэше и для показа в дельте.</summary>
    public static string? ValueOf(in ParamValue value) => value.Display ?? value.Raw;
}
