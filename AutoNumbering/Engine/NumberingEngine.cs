using Autodesk.Revit.DB;

namespace AutoNumbering.Engine;

public class NumberingEngine
{
    private readonly Document _doc;

    public NumberingEngine(Document doc) => _doc = doc;

    public (int succeeded, int failed, List<string> errors) Apply(
        IEnumerable<ElementItem> items, NumberingConfig config)
    {
        int succeeded = 0, failed = 0;
        var errors = new List<string>();

        foreach (ElementItem item in items.Where(i => i.IsIncluded))
        {
            try
            {
                Element element = _doc.GetElement(item.Id);
                if (element is null) { failed++; continue; }

                if (!TryWriteParameter(element, config.TargetParameter, item.ProposedNumber))
                {
                    errors.Add($"Element {item.Id.Value}: '{config.TargetParameter}' not writable.");
                    failed++;
                }
                else
                {
                    succeeded++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Element {item.Id.Value}: {ex.Message}");
                failed++;
            }
        }

        return (succeeded, failed, errors);
    }

    private static bool TryWriteParameter(Element element, string paramName, string value)
    {
        Parameter? p = paramName.Equals("Mark", StringComparison.OrdinalIgnoreCase)
            ? element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)
            : null;

        p ??= element.LookupParameter(paramName);

        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String)
            return false;

        p.Set(value);
        return true;
    }

    public static string FormatNumber(int number, NumberingConfig config) =>
        $"{config.Prefix}{number.ToString().PadLeft(config.MinDigits, '0')}{config.Suffix}";

    public static List<ElementItem> BuildItems(
        Document doc,
        IList<ElementId> ids,
        string targetParam,
        string sortParam,
        bool sortDescending)
    {
        var items = ids
            .Select(id => doc.GetElement(id))
            .Where(e => e is not null)
            .Select(e => new ElementItem
            {
                Id = e!.Id,
                Category = e.Category?.Name ?? string.Empty,
                FamilyType = GetTypeName(e),
                CurrentValue = GetParamValue(e, targetParam),
                SortKeyValue = string.IsNullOrEmpty(sortParam)
                    ? string.Empty
                    : GetParamValue(e, sortParam),
            })
            .ToList();

        if (!string.IsNullOrEmpty(sortParam))
        {
            items = sortDescending
                ? items.OrderByDescending(i => i.SortKeyValue, NaturalComparer.Instance).ToList()
                : items.OrderBy(i => i.SortKeyValue, NaturalComparer.Instance).ToList();
        }

        return items;
    }

    /// <summary>
    /// Returns writable string parameters from the first element, always including Mark.
    /// </summary>
    public static IList<string> GetWritableStringParams(Document doc, IList<ElementId> ids)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "Mark" };

        if (ids.Count == 0) return names.ToList();
        Element? elem = doc.GetElement(ids[0]);
        if (elem is null) return names.ToList();

        foreach (Parameter p in elem.Parameters)
        {
            if (p.IsReadOnly || p.StorageType != StorageType.String) continue;
            string name = p.Definition?.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names.ToList();
    }

    /// <summary>
    /// Returns all readable parameters from the first element (for sorting).
    /// </summary>
    public static IList<string> GetAllReadableParams(Document doc, IList<ElementId> ids)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (ids.Count == 0) return names.ToList();
        Element? elem = doc.GetElement(ids[0]);
        if (elem is null) return names.ToList();

        foreach (Parameter p in elem.Parameters)
        {
            string name = p.Definition?.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names.ToList();
    }

    public static string GetParamValue(Element element, string paramName)
    {
        Parameter? p = paramName.Equals("Mark", StringComparison.OrdinalIgnoreCase)
            ? element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)
            : null;

        p ??= element.LookupParameter(paramName);
        if (p is null) return string.Empty;

        return p.StorageType switch
        {
            StorageType.String => p.AsString() ?? string.Empty,
            StorageType.Integer => p.AsInteger().ToString(),
            StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("G4"),
            StorageType.ElementId => p.AsValueString() ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static string GetTypeName(Element element)
    {
        ElementId typeId = element.GetTypeId();
        if (typeId == ElementId.InvalidElementId) return string.Empty;
        return element.Document.GetElement(typeId)?.Name ?? string.Empty;
    }
}
