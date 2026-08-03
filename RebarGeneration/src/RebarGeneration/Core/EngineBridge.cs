using System.Collections;
using System.Reflection;
using Autodesk.Revit.DB;
using RebarGeneration.Contracts;

// UseWPF заменяет стандартный набор implicit usings: System.IO в нём нет, а
// System.Windows.Shapes приносит собственный Path — импорт явный, с алиасами.
using File = System.IO.File;
using Path = System.IO.Path;

namespace RebarGeneration.Core;

/// <summary>
/// Позднее связывание с движками соседних плагинов.
/// <para>
/// Связь намеренно через рефлексию, а не через <c>ProjectReference</c>. Причины:
/// </para>
/// <list type="bullet">
/// <item>плагин остаётся самостоятельным — его можно поставить, не имея
/// SlabReinforcement/WallReinforcement/ColumnReinforcement, и он честно скажет,
/// какого движка не хватает, вместо падения загрузчика сборок;</item>
/// <item>CI собирает RebarGeneration, не втягивая три чужих проекта;</item>
/// <item>движки и так уже загружены в процесс Revit своими манифестами —
/// платить за это ещё и жёсткой зависимостью незачем.</item>
/// </list>
/// <para>
/// Плата за это — хрупкость к смене сигнатур. Она смягчена тем, что метод
/// ищется по имени и числу параметров, а любое несовпадение превращается в
/// внятную ошибку с кодом, а не в <c>NullReferenceException</c> где-то внутри.
/// </para>
/// </summary>
public static class EngineBridge
{
    /// <summary>Тип из уже загруженной сборки, либо <c>null</c>.</summary>
    public static Type? FindType(string assemblyName, string fullTypeName)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            string? n = null;
            try { n = a.GetName().Name; } catch { /* динамические сборки */ }
            if (!string.Equals(n, assemblyName, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                Type? t = a.GetType(fullTypeName, throwOnError: false);
                if (t is not null) return t;
            }
            catch { /* повреждённая сборка — просто не наш случай */ }
        }
        return null;
    }

    public static Type RequireType(string assemblyName, string fullTypeName) =>
        FindType(assemblyName, fullTypeName)
        ?? throw new JobException("ENGINE_MISSING",
            $"движок не найден: тип {fullTypeName} из сборки {assemblyName}. "
            + $"Плагин {assemblyName} не установлен или не загружен в этот сеанс Revit.");

    /// <summary>Загрузить конфиг движка его же загрузчиком.</summary>
    public static object LoadConfig(string assemblyName, string loaderTypeName, string path)
    {
        if (!File.Exists(path))
            throw new JobException("NO_CONFIG_FILE", $"конфиг движка не найден: {path}");

        Type loader = RequireType(assemblyName, loaderTypeName);
        MethodInfo load = loader.GetMethod("Load", BindingFlags.Public | BindingFlags.Static,
                                           [typeof(string)])
            ?? throw new JobException("ENGINE_API_CHANGED",
                $"{loaderTypeName}.Load(string) не найден — сигнатура движка изменилась");
        try
        {
            return load.Invoke(null, [path])
                ?? throw new JobException("BAD_CONFIG", $"{loaderTypeName}.Load вернул null для {path}");
        }
        catch (TargetInvocationException ex)
        {
            throw new JobException("BAD_CONFIG",
                $"движок не принял конфиг {Path.GetFileName(path)}: {Inner(ex)}");
        }
    }

    /// <summary>Создать экземпляр движка от документа.</summary>
    public static object NewEngine(Type engineType, Document doc)
    {
        ConstructorInfo ctor = engineType.GetConstructor([typeof(Document)])
            ?? throw new JobException("ENGINE_API_CHANGED",
                $"{engineType.FullName}(Document) не найден — сигнатура движка изменилась");
        return ctor.Invoke([doc]);
    }

    /// <summary>Найти метод по имени и точному списку типов параметров.</summary>
    public static MethodInfo RequireMethod(Type t, string name, int parameterCount)
    {
        MethodInfo? m = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(x => x.Name == name && x.GetParameters().Length == parameterCount);
        return m ?? throw new JobException("ENGINE_API_CHANGED",
            $"{t.FullName}.{name} с {parameterCount} параметрами не найден — сигнатура движка изменилась");
    }

    public static object Invoke(MethodInfo m, object target, params object?[] args)
    {
        try
        {
            return m.Invoke(target, args)
                ?? throw new JobException("ENGINE_FAILED", $"{m.Name} вернул null");
        }
        catch (TargetInvocationException ex)
        {
            throw new JobException("ENGINE_FAILED", $"{m.Name}: {Inner(ex)}");
        }
    }

    /// <summary>
    /// Прочитать общий для трёх движков <c>RunResult</c>: у него везде есть
    /// <c>Succeeded/Skipped/Failed/TotalCreated</c> и коллекция <c>Outcomes</c>
    /// с <c>Reason</c>. Читаем по именам, отсутствующее считаем нулём — отчёт
    /// не должен разваливаться из-за того, что у одного движка полем больше.
    /// </summary>
    public static DelegatedResult ReadRunResult(object runResult)
    {
        int created = Int(runResult, "TotalCreated");
        int skipped = Int(runResult, "Skipped");
        int failed = Int(runResult, "Failed");
        int replaced = Int(runResult, "TotalReplaced");

        var notes = new List<string>();
        if (replaced > 0) notes.Add($"движок заменил уже существующую арматуру: {replaced}");

        if (runResult.GetType().GetProperty("Outcomes")?.GetValue(runResult) is IEnumerable outcomes)
            foreach (object? o in outcomes)
            {
                if (o is null) continue;
                string? reason = o.GetType().GetProperty("Reason")?.GetValue(o) as string;
                object? status = o.GetType().GetProperty("Status")?.GetValue(o);
                if (!string.IsNullOrWhiteSpace(reason))
                    notes.Add($"{status}: {reason}");
                if (notes.Count >= 12) { notes.Add("…"); break; }
            }

        return new DelegatedResult(created, skipped, failed, notes);
    }

    private static int Int(object target, string prop)
    {
        object? v = target.GetType().GetProperty(prop)?.GetValue(target);
        return v is int i ? i : 0;
    }

    private static string Inner(TargetInvocationException ex) =>
        ex.InnerException?.Message ?? ex.Message;

    /// <summary>
    /// Построить <c>Dictionary&lt;ElementId, TConfig&gt;</c>, где TConfig известен
    /// только в рантайме, — иначе движок с per-host словарём не вызвать.
    /// </summary>
    public static object MakeIdMap(Type configType, IReadOnlyList<ElementId> ids, object config)
    {
        Type dictType = typeof(Dictionary<,>).MakeGenericType(typeof(ElementId), configType);
        object dict = Activator.CreateInstance(dictType)!;
        MethodInfo add = dictType.GetMethod("Add", [typeof(ElementId), configType])!;
        foreach (ElementId id in ids) add.Invoke(dict, [id, config]);
        return dict;
    }

    /// <summary>Пустой <c>List&lt;T&gt;</c> для необязательных параметров движка.</summary>
    public static object EmptyList(Type itemType) =>
        Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;

    /// <summary>Список ElementId в типизированном виде — движки принимают IEnumerable&lt;ElementId&gt;.</summary>
    public static List<ElementId> Ids(IReadOnlyList<ElementId> ids) => [.. ids];
}
