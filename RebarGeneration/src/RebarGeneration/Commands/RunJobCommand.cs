using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace RebarGeneration.Commands;

/// <summary>
/// Кнопка «Run Job»: выбрать файл задания и выполнить его.
/// <para>
/// Никакой отдельной логики здесь нет — команда вызывает тот же
/// <see cref="Api"/>, что и агент. Один движок, три транспорта: лента, <c>/exec</c>,
/// будущий MCP-инструмент. Расхождение поведения между «руками» и «агентом»
/// исключено по построению.
/// </para>
/// </summary>
[Transaction(TransactionMode.Manual)]
public class RunJobCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => JobCommandRunner.Execute(commandData, ref message, dryRun: false);
}

/// <summary>Кнопка «Dry Run»: то же задание, но с гарантированным откатом.</summary>
[Transaction(TransactionMode.Manual)]
public class DryRunJobCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => JobCommandRunner.Execute(commandData, ref message, dryRun: true);
}

internal static class JobCommandRunner
{
    internal static Result Execute(ExternalCommandData commandData, ref string message, bool dryRun)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;

        var dlg = new OpenFileDialog
        {
            Title = dryRun ? "Задание для холостого прогона" : "Задание на генерацию арматуры",
            Filter = "Rebar job (*.json)|*.json|Все файлы (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return Result.Cancelled;

        string json = dryRun ? Api.DryRun(doc, dlg.FileName) : Api.Run(doc, dlg.FileName);
        Show(json, dryRun);
        return Result.Succeeded;
    }

    private static void Show(string reportJson, bool dryRun)
    {
        string summary;
        try
        {
            using JsonDocument d = JsonDocument.Parse(reportJson);
            JsonElement r = d.RootElement;
            bool ok = r.TryGetProperty("ok", out JsonElement okEl) && okEl.GetBoolean();

            if (r.TryGetProperty("counts", out JsonElement c))
                summary = $"{(ok ? "Готово" : "С ошибками")}: "
                          + $"групп {Int(c, "groups")}, наборов {Int(c, "sets")}, "
                          + $"стержней {Int(c, "bars")}, пропущено {Int(c, "skipped")}, "
                          + $"ошибок {Int(c, "failed")}."
                          + $"\nТранзакций: {Int(r, "transactions")}, время: {Int(r, "elapsedMs")} мс.";
            else
                summary = ok ? "Готово." : "Задание не выполнено.";

            if (r.TryGetProperty("errors", out JsonElement errs) && errs.GetArrayLength() > 0)
            {
                summary += "\n\nОшибки:";
                foreach (JsonElement e in errs.EnumerateArray().Take(10))
                    summary += $"\n• {e.GetProperty("message").GetString()}";
            }
        }
        catch
        {
            summary = reportJson.Length > 2000 ? reportJson[..2000] + "…" : reportJson;
        }

        var td = new TaskDialog(dryRun ? "Rebar Generation — Dry Run" : "Rebar Generation")
        {
            MainInstruction = dryRun ? "Холостой прогон завершён (модель не изменена)" : "Прогон завершён",
            MainContent = summary,
            ExpandedContent = reportJson.Length > 20000 ? reportJson[..20000] + "…" : reportJson,
        };
        td.Show();
    }

    private static long Int(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement v) && v.TryGetInt64(out long n) ? n : 0;
}
