using RebarGeneration.Contracts;
using RebarGeneration.Generators;

namespace RebarGeneration.Core;

/// <summary>
/// Реестр генераторов — единственная точка, где универсальная оболочка узнаёт о
/// предметных генераторах.
/// <para>
/// Добавить поддержку нового типа хоста = написать <see cref="IRebarGenerator"/> и
/// зарегистрировать его здесь. Всё остальное — транзакции, ключи, идемпотентность,
/// отчёт, журнал — он получает даром.
/// </para>
/// <para>
/// Обёртки над уже существующими движками (<c>SlabReinforcer</c>,
/// <c>WallReinforcer</c>, <c>ColumnReinforcer</c>) регистрируются здесь же, когда
/// дойдут руки: они умеют считать геометрию лучше любого универсального кода, и
/// переписывать их незачем.
/// </para>
/// </summary>
public static class GeneratorRegistry
{
    private static readonly Dictionary<string, IRebarGenerator> Planning =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["polyline"] = new PolylineGenerator(),
            ["footing"] = new FootingGenerator(),
        };

    private static readonly Dictionary<string, IDelegatingGenerator> Delegating =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["slab"] = new SlabEngineGenerator(),
            ["wall"] = new WallEngineGenerator(),
            ["column"] = new ColumnEngineGenerator(),
        };

    public static IReadOnlyCollection<string> Names =>
        [.. Planning.Keys, .. Delegating.Keys];

    /// <summary>Делегирующие генераторы отделены явно: раннер обязан вызывать их
    /// ВНЕ своих транзакций, потому что движки открывают свои.</summary>
    public static bool IsDelegating(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Delegating.ContainsKey(name);

    public static IRebarGenerator Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new JobException("NO_GENERATOR", "не указан generator");

        if (Planning.TryGetValue(name, out IRebarGenerator? gen)) return gen;

        throw new JobException("UNKNOWN_GENERATOR", Unknown(name));
    }

    public static IDelegatingGenerator GetDelegating(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new JobException("NO_GENERATOR", "не указан generator");

        if (Delegating.TryGetValue(name, out IDelegatingGenerator? gen)) return gen;

        throw new JobException("UNKNOWN_GENERATOR", Unknown(name));
    }

    private static string Unknown(string name) =>
        $"генератор '{name}' не зарегистрирован; есть: {string.Join(", ", Names.OrderBy(n => n))}";

    /// <summary>Зарегистрировать генератор извне (в том числе из другого плагина).</summary>
    public static void Register(IRebarGenerator generator) => Planning[generator.Name] = generator;

    public static void Register(IDelegatingGenerator generator) => Delegating[generator.Name] = generator;

    /// <summary>Какие делегирующие движки реально доступны в этом сеансе Revit —
    /// уходит в <c>Api.Info</c>, чтобы агент не составлял задание вслепую.</summary>
    public static Dictionary<string, bool> DelegatingAvailability()
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { loaded.Add(a.GetName().Name ?? string.Empty); }
            catch { /* динамические сборки */ }
        }
        return Delegating.ToDictionary(kv => kv.Key, kv => loaded.Contains(kv.Value.RequiredAssembly));
    }
}
