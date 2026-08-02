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
    private static readonly Dictionary<string, IRebarGenerator> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["polyline"] = new PolylineGenerator(),
            ["footing"] = new FootingGenerator(),
        };

    public static IReadOnlyCollection<string> Names => Registry.Keys;

    public static IRebarGenerator Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new JobException("NO_GENERATOR", "не указан generator");

        if (Registry.TryGetValue(name, out IRebarGenerator? gen)) return gen;

        throw new JobException("UNKNOWN_GENERATOR",
            $"генератор '{name}' не зарегистрирован; есть: {string.Join(", ", Registry.Keys)}");
    }

    /// <summary>Зарегистрировать генератор извне (в том числе из другого плагина).</summary>
    public static void Register(IRebarGenerator generator) => Registry[generator.Name] = generator;
}
