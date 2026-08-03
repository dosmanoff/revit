using System.Reflection;
using Autodesk.Revit.DB;
using RebarGeneration.Contracts;
using RebarGeneration.Core;

namespace RebarGeneration.Generators;

/// <summary>
/// Стены — делегирование <c>WallReinforcement.Engine.WallReinforcer</c>.
/// Движок открывает по транзакции на стену и ждёт внешний <c>TransactionGroup</c>,
/// который даёт раннер.
/// </summary>
public sealed class WallEngineGenerator : IDelegatingGenerator
{
    public string Name => "wall";
    public string RequiredAssembly => "WallReinforcement";

    public DelegatedResult Build(GroupSpec g, DelegatedContext ctx)
    {
        object cfg = EngineBridge.LoadConfig(
            RequiredAssembly, "WallReinforcement.Config.ConfigLoader", g.ConfigPath!);

        Type engineType = EngineBridge.RequireType(
            RequiredAssembly, "WallReinforcement.Engine.WallReinforcer");
        object engine = EngineBridge.NewEngine(engineType, ctx.Doc);

        MethodInfo run = EngineBridge.RequireMethod(engineType, "Run", 3);
        object result = EngineBridge.Invoke(
            run, engine, EngineBridge.Ids(ctx.Hosts), cfg, ctx.DryRun);

        return EngineBridge.ReadRunResult(result);
    }
}

/// <summary>
/// Колонны — делегирование <c>ColumnReinforcement.Engine.ColumnReinforcer</c>.
/// Тот же контракт по транзакциям, что и у стен (движок это прямо документирует).
/// </summary>
public sealed class ColumnEngineGenerator : IDelegatingGenerator
{
    public string Name => "column";
    public string RequiredAssembly => "ColumnReinforcement";

    public DelegatedResult Build(GroupSpec g, DelegatedContext ctx)
    {
        object cfg = EngineBridge.LoadConfig(
            RequiredAssembly, "ColumnReinforcement.Config.ConfigLoader", g.ConfigPath!);

        Type engineType = EngineBridge.RequireType(
            RequiredAssembly, "ColumnReinforcement.Engine.ColumnReinforcer");
        object engine = EngineBridge.NewEngine(engineType, ctx.Doc);

        MethodInfo run = EngineBridge.RequireMethod(engineType, "Run", 3);
        object result = EngineBridge.Invoke(
            run, engine, EngineBridge.Ids(ctx.Hosts), cfg, ctx.DryRun);

        return EngineBridge.ReadRunResult(result);
    }
}

/// <summary>
/// Плиты — делегирование <c>SlabReinforcement.Engine.SlabReinforcer</c>.
/// <para>
/// Сигнатура сложнее двух других: движок берёт словарь «плита → конфиг», список
/// зон и параметры брифа. Здесь передаётся один и тот же конфиг на все хосты,
/// зоны пустые, бриф отсутствует — то есть режим «один конфиг на выделение»,
/// как в диалоге плагина. Разбор JSON-брифа с раскладкой по маркам плит
/// сознательно не дублируется: он живёт в самом SlabReinforcement, и вытаскивать
/// его сюда рефлексией значило бы копировать логику, которая может разойтись.
/// </para>
/// </summary>
public sealed class SlabEngineGenerator : IDelegatingGenerator
{
    public string Name => "slab";
    public string RequiredAssembly => "SlabReinforcement";

    public DelegatedResult Build(GroupSpec g, DelegatedContext ctx)
    {
        object cfg = EngineBridge.LoadConfig(
            RequiredAssembly, "SlabReinforcement.Config.ConfigLoader", g.ConfigPath!);

        Type engineType = EngineBridge.RequireType(
            RequiredAssembly, "SlabReinforcement.Engine.SlabReinforcer");
        object engine = EngineBridge.NewEngine(engineType, ctx.Doc);

        MethodInfo run = EngineBridge.RequireMethod(engineType, "Run", 5);
        ParameterInfo[] ps = run.GetParameters();

        // perSlab: Dictionary<ElementId, SlabReinforcementConfig> — тип конфига
        // известен только в рантайме, поэтому словарь строится рефлексией.
        object perSlab = EngineBridge.MakeIdMap(cfg.GetType(), ctx.Hosts, cfg);

        // zones: пустой список — зоны здесь не задаются
        Type zoneItem = ps[1].ParameterType.IsGenericType
            ? ps[1].ParameterType.GetGenericArguments()[0]
            : typeof(object);
        object zones = EngineBridge.EmptyList(zoneItem);

        // briefs = null, briefUnits = значение по умолчанию из сигнатуры
        object? briefUnits = ps[4].HasDefaultValue
            ? ps[4].DefaultValue
            : Activator.CreateInstance(ps[4].ParameterType);

        object result = EngineBridge.Invoke(
            run, engine, perSlab, zones, ctx.DryRun, null, briefUnits);

        return EngineBridge.ReadRunResult(result);
    }
}
