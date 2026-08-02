using Autodesk.Revit.DB;
using RebarGeneration.Contracts;
using RebarGeneration.Core;

namespace RebarGeneration;

/// <summary>
/// Единственная публичная дверь плагина: <b>строка внутрь — строка наружу</b>.
/// <para>
/// Сигнатура выбрана именно такой намеренно. Агент вызывает плагин из pyRevit
/// Routes <c>/exec</c>, то есть из IronPython, и любой .NET-генерик на границе
/// (<c>IEnumerable&lt;ElementId&gt;</c>, <c>IDictionary&lt;,&gt;</c>) превращается
/// в возню с маршалингом. JSON-строка убирает эту проблему целиком: вызов из
/// IronPython — две строки без единого генерика.
/// </para>
/// <para>
/// Тот же фасад бесплатно даёт три транспорта: <c>/exec</c> сегодня, команда в
/// commandset для настоящего MCP-инструмента завтра, кнопка на ленте для
/// человека. Третий мост между агентом и Revit строить не нужно.
/// </para>
/// <para>
/// Наружу НИКОГДА не летит исключение: любая ошибка возвращается как
/// <c>{"ok": false, "errors": [...]}</c>. Через границу IronPython исключение
/// приходит без внятного текста, и отладка превращается в гадание.
/// </para>
/// </summary>
public static class Api
{
    /// <summary>Версия контракта. Пусть вызывающая сторона сможет свериться.</summary>
    public const string Version = "0.1.0";

    /// <summary>Выполнить задание из файла. Возвращает JSON-отчёт.</summary>
    public static string Run(Document doc, string jobJsonPath) =>
        Guard(() =>
        {
            Job job = JobIo.Load(jobJsonPath);
            return new JobRunner(doc).Run(job, jobJsonPath);
        });

    /// <summary>Выполнить задание, переданное строкой. Возвращает JSON-отчёт.</summary>
    public static string RunJson(Document doc, string jobJson) =>
        Guard(() =>
        {
            Job job = JobIo.Parse(jobJson);
            return new JobRunner(doc).Run(job, null);
        });

    /// <summary>
    /// Прогнать задание вхолостую независимо от того, что написано в самом задании:
    /// строится всё, затем откатывается. Отчёт настоящий, модель не меняется.
    /// </summary>
    public static string DryRun(Document doc, string jobJsonPath) =>
        Guard(() =>
        {
            Job job = JobIo.Load(jobJsonPath);
            job.DryRun = true;
            return new JobRunner(doc).Run(job, jobJsonPath);
        });

    /// <summary>
    /// Проверить задание без Revit-части: схема, единицы, дубли ключей, геометрия
    /// групп. Не открывает транзакций и не трогает модель.
    /// </summary>
    public static string Validate(string jobJsonPath) =>
        Guard(() =>
        {
            Job job = JobIo.Load(jobJsonPath);
            IReadOnlyList<ReportError> errors = JobValidator.Validate(job);
            return new RunReport
            {
                Ok = errors.Count == 0,
                Mode = "validate",
                Counts = new RunCounts { Groups = job.Groups.Count },
                Errors = [.. errors],
            };
        });

    /// <summary>
    /// Контекст модели для написания задания: система координат, доступные типы
    /// стержней и крюков, габариты запрошенных хостов.
    /// <para>
    /// Отдаёт ровно то, что нужно для конфига, и НЕ отдаёт полную геометрию:
    /// смысл в том, чтобы агент получил основание для решения, а не дамп модели
    /// в контекст.
    /// </para>
    /// </summary>
    /// <param name="hostIdsCsv">Список ElementId через запятую. Пусто — только общая часть.</param>
    public static string Describe(Document doc, string? hostIdsCsv = null) =>
        GuardJson(() => ContextDump.Build(doc, ParseIds(hostIdsCsv)));

    /// <summary>Список зарегистрированных генераторов и версия плагина.</summary>
    public static string Info() => GuardJson(() => new
    {
        ok = true,
        plugin = "RebarGeneration",
        version = Version,
        jobSchema = Job.CurrentSchema,
        reportSchema = RunReport.CurrentSchema,
        generators = GeneratorRegistry.Names.OrderBy(n => n).ToArray(),
        // Делегирующие генераторы работают, только если соответствующий плагин
        // загружен в этот сеанс. Показываем это сразу, чтобы задание не
        // составлялось вслепую.
        engines = GeneratorRegistry.DelegatingAvailability(),
    });

    // ------------------------------------------------------------------ обвязка

    private static string Guard(Func<RunReport> body)
    {
        try
        {
            return JobIo.ToJson(body());
        }
        catch (JobException ex)
        {
            return JobIo.ToJson(RunReport.Failure(ex.Code, ex.Message));
        }
        catch (Exception ex)
        {
            return JobIo.ToJson(RunReport.Failure("UNEXPECTED", $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    private static string GuardJson(Func<object> body)
    {
        try
        {
            return JobIo.ToJson(body());
        }
        catch (JobException ex)
        {
            return JobIo.ToJson(new { ok = false, error = ex.Code, message = ex.Message });
        }
        catch (Exception ex)
        {
            return JobIo.ToJson(new { ok = false, error = "UNEXPECTED", message = $"{ex.GetType().Name}: {ex.Message}" });
        }
    }

    private static IReadOnlyList<long> ParseIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        var ids = new List<long>();
        foreach (string part in csv.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            if (long.TryParse(part.Trim(), out long id)) ids.Add(id);
            else throw new JobException("BAD_ID", $"'{part}' не похоже на ElementId");
        return ids;
    }
}
