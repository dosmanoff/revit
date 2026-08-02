using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RebarGeneration.Contracts;

namespace RebarGeneration.Core;

/// <summary>
/// Типы стержней и крюков, собранные ОДНИМ проходом коллектора на весь прогон.
/// <para>
/// Зачем отдельный класс: старый поштучный путь строил новый
/// <c>FilteredElementCollector</c> по <c>RebarBarType</c> и дважды по
/// <c>RebarHookType</c> на КАЖДЫЙ стержень. На тысяче стержней это три тысячи
/// обходов ради данных, которые не меняются в течение прогона.
/// </para>
/// <para>
/// Поиск типа стержня строгий: если запрошенного размера нет, прогон падает с
/// внятной ошибкой, а не подставляет молча другой диаметр. Крюки, наоборот,
/// не строгие — «нет крюка» это законная геометрия.
/// </para>
/// </summary>
public sealed class TypeCache
{
    private readonly Dictionary<string, RebarBarType> _bars = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RebarHookType> _hooks = [];
    private readonly Dictionary<string, RebarHookType?> _hookCache = new(StringComparer.OrdinalIgnoreCase);

    public TypeCache(Document doc)
    {
        foreach (RebarBarType t in new FilteredElementCollector(doc)
                     .OfClass(typeof(RebarBarType)).Cast<RebarBarType>())
            _bars[t.Name] = t;

        _hooks.AddRange(new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>());
    }

    public IReadOnlyCollection<string> BarTypeNames => _bars.Keys;

    public IReadOnlyList<string> HookTypeNames => _hooks.ConvertAll(h => h.Name);

    /// <summary>Строгий поиск типа стержня. Никаких молчаливых подстановок.</summary>
    public RebarBarType BarType(string name)
    {
        if (_bars.TryGetValue(name, out RebarBarType? t)) return t;
        string known = string.Join(", ", _bars.Keys.OrderBy(k => k).Take(24));
        throw new JobException("NO_BAR_TYPE",
            $"тип стержня '{name}' не найден в модели; есть: {known}");
    }

    /// <summary>
    /// Крюк по имени или фрагменту имени ("135", "90"). <c>null</c> — прямой конец.
    /// Точное совпадение имеет приоритет над совпадением по фрагменту, иначе
    /// "90" случайно подцепит "190".
    /// </summary>
    public RebarHookType? Hook(string? nameOrFragment)
    {
        if (string.IsNullOrWhiteSpace(nameOrFragment)) return null;
        if (_hookCache.TryGetValue(nameOrFragment, out RebarHookType? cached)) return cached;

        RebarHookType? hit =
            _hooks.FirstOrDefault(h => string.Equals(h.Name, nameOrFragment, StringComparison.OrdinalIgnoreCase))
            ?? _hooks.FirstOrDefault(h => h.Name.Contains(nameOrFragment, StringComparison.OrdinalIgnoreCase));

        _hookCache[nameOrFragment] = hit;
        return hit;
    }

    /// <summary>Номинальный диаметр типа стержня, футы — нужен для отступа слоёв сетки.</summary>
    public double DiameterFt(string name) => BarType(name).BarNominalDiameter;
}
