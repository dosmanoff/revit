using Autodesk.Revit.DB;
using RebarGeneration.Contracts;

namespace RebarGeneration.Core;

/// <summary>
/// Индекс «стабильный ключ → элемент», построенный ОДНИМ проходом.
/// <para>
/// Это замена <c>keys.find_by_key</c> из revit-context, который на каждый
/// создаваемый стержень заново обходил всю категорию <c>OST_Rebar</c> и читал
/// параметры каждого найденного. На N стержнях получалось O(N²): для тысячи
/// стержней — полмиллиона чтений параметров. Здесь тот же обход делается один
/// раз, дальше поиск идёт по словарю.
/// </para>
/// <para>
/// Формат ключа намеренно совпадает с revit-context: сначала выделенный общий
/// параметр <c>CTX_Key</c>, иначе токен <c>[[ctx:KEY]]</c> внутри Comments,
/// причём остальной текст Comments сохраняется — там живут марки вида "8#7 [1]".
/// Совпадение важно: иначе два инструмента будут считать одну и ту же модель
/// по-разному и идемпотентность развалится.
/// </para>
/// </summary>
public sealed class KeyIndex
{
    public const string SharedKeyParam = "CTX_Key";

    private readonly Dictionary<string, ElementId> _byKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);

    private KeyIndex() { }

    /// <summary>Построить индекс по категориям (по умолчанию — арматура).</summary>
    public static KeyIndex Build(Document doc, params BuiltInCategory[] categories)
    {
        var idx = new KeyIndex();
        BuiltInCategory[] cats = categories.Length > 0 ? categories : [BuiltInCategory.OST_Rebar];

        foreach (BuiltInCategory cat in cats)
        foreach (Element e in new FilteredElementCollector(doc)
                     .OfCategory(cat).WhereElementIsNotElementType())
        {
            string? key = ReadKey(e);
            if (key is null) continue;
            if (!idx._byKey.TryAdd(key, e.Id)) idx._ambiguous.Add(key);
        }

        return idx;
    }

    public int Count => _byKey.Count;

    /// <summary>Ключи, которые в модели носят несколько элементов. Молча брать
    /// «первый попавшийся» нельзя — правка уйдёт не в тот элемент.</summary>
    public IReadOnlyCollection<string> AmbiguousKeys => _ambiguous;

    public bool TryGet(string key, out ElementId id)
    {
        if (_byKey.TryGetValue(key, out ElementId? found) && found is not null)
        {
            id = found;
            return true;
        }
        id = ElementId.InvalidElementId;
        return false;
    }

    public bool Contains(string key) => _byKey.ContainsKey(key);

    public bool IsAmbiguous(string key) => _ambiguous.Contains(key);

    /// <summary>Зарегистрировать только что созданный элемент — чтобы повтор ключа
    /// внутри одного прогона тоже ловился.</summary>
    public void Add(string key, ElementId id) => _byKey[key] = id;

    public void Remove(string key) => _byKey.Remove(key);

    // ------------------------------------------------------------ чтение/запись

    public static string? ReadKey(Element e)
    {
        Parameter? shared = e.LookupParameter(SharedKeyParam);
        if (shared is not null && shared.HasValue)
        {
            string? v = shared.AsString();
            if (!string.IsNullOrEmpty(v)) return v;
        }

        Parameter? comments = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments is not null && comments.HasValue)
            return KeyToken.Parse(comments.AsString());

        return null;
    }

    /// <summary>
    /// Записать ключ. Вызывать ТОЛЬКО внутри транзакции. Возвращает использованное
    /// хранилище (<c>shared</c> / <c>comments</c>) или <c>null</c>, если записать
    /// не удалось — вызывающий код обязан считать это отказом, а не мелочью:
    /// элемент без ключа выпадает из идемпотентности и продублируется на следующем
    /// прогоне.
    /// </summary>
    public static string? WriteKey(Element e, string key)
    {
        Parameter? shared = e.LookupParameter(SharedKeyParam);
        if (shared is not null && !shared.IsReadOnly && shared.StorageType == StorageType.String)
            if (shared.Set(key))
                return "shared";

        Parameter? comments = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments is null || comments.IsReadOnly) return null;

        string existing = comments.AsString() ?? string.Empty;
        return comments.Set(KeyToken.Merge(existing, key)) ? "comments" : null;
    }
}
