using Autodesk.Revit.DB;
using RebarGeneration.Contracts;

namespace RebarGeneration.Core;

/// <summary>
/// Граница между «понимать хост» и «уметь класть арматуру».
/// <para>
/// Генератор занимается ТОЛЬКО геометрией: получает группу задания и контекст
/// хоста, возвращает список наборов в футах. Он не открывает транзакций, не
/// ищет типы стержней, не пишет ключи и ничего не знает про идемпотентность —
/// это всё делает <see cref="JobRunner"/> одинаково для всех генераторов.
/// </para>
/// <para>
/// Именно из-за этого разделения универсальным получается каркас, а не предметная
/// область: плита, стена, колонна и ростверк считаются по-разному, а вот
/// транзакции, ключи, наборы и отчёт у них общие.
/// </para>
/// </summary>
public interface IRebarGenerator
{
    /// <summary>Имя, которым генератор адресуется в поле <c>generator</c> задания.</summary>
    string Name { get; }

    /// <summary>
    /// Разложить группу в наборы. Бросить <see cref="JobException"/>, если
    /// группу нельзя построить — раннер превратит это в <c>failed</c> без срыва
    /// остальных групп.
    /// </summary>
    IReadOnlyList<PlannedSet> Plan(GroupSpec group, BuildContext ctx);
}

/// <summary>
/// Один запланированный набор: форма представительного стержня плюс правило
/// размножения. Чистые данные в футах — Revit-объекты появляются уже в раннере.
/// </summary>
public sealed record PlannedSet(
    IReadOnlyList<Vec3> Polyline,
    string BarType,
    int Count,
    double SpacingFt,
    Vec3? Distribution = null,
    string? HookStart = null,
    string? HookEnd = null,
    string? Suffix = null)
{
    /// <summary>Длина одного стержня, футы.</summary>
    public double BarLengthFt => LayoutMath.PolylineLength(Polyline);

    /// <summary>Стержней в наборе (минимум 1).</summary>
    public int Bars => Math.Max(1, Count);
}

/// <summary>
/// Всё, что генератору нужно знать о хосте и о задании, — в одном месте, чтобы
/// генераторы не таскали за собой <see cref="Document"/> и не расходились в том,
/// как считается защитный слой.
/// </summary>
public sealed class BuildContext
{
    public required Document Doc { get; init; }
    public required Element Host { get; init; }
    public required LengthUnits.Converter U { get; init; }
    public required GroupDefaults Defaults { get; init; }

    /// <summary>Типы стержней и крюков одного прогона. Генераторам нужен хотя бы
    /// номинальный диаметр — по нему считается отступ слоёв сетки.</summary>
    public required TypeCache Types { get; init; }

    /// <summary>Куда генератор пишет то, о чём обязан предупредить, но что не
    /// является отказом: например «контур не снялся, считаю по габариту».
    /// Молчаливый откат на худший алгоритм — это скрытая ошибка в результате.</summary>
    public List<string> Notes { get; } = [];

    private BoundingBoxXYZ? _box;

    /// <summary>Габарит хоста в координатах модели (футы). Кэшируется: обращение
    /// к <c>get_BoundingBox</c> не бесплатно, а генераторы спрашивают его часто.</summary>
    public BoundingBoxXYZ Box => _box ??= Host.get_BoundingBox(null)
        ?? throw new JobException("NO_BBOX", $"у хоста {Host.Id} нет габаритного бокса");

    /// <summary>Тип стержня группы с откатом на defaults.</summary>
    public string BarTypeOf(GroupSpec g, string? local = null)
    {
        string? name = local ?? g.BarType ?? Defaults.BarType;
        if (string.IsNullOrWhiteSpace(name))
            throw new JobException("NO_BAR_TYPE", $"{g.Key}: не задан barType и нет defaults.barType");
        return name;
    }

    /// <summary>Защитный слой группы в футах, с откатом на defaults и на 0.</summary>
    public double CoverFt(GroupSpec g) => U.Ft(g.Cover ?? Defaults.Cover ?? 0.0);

    /// <summary>Крюки группы с откатом на defaults.</summary>
    public (string? Start, string? End) HooksOf(GroupSpec g)
    {
        HookSpec? h = g.Hooks ?? Defaults.Hooks;
        return (h?.Start, h?.End);
    }
}
