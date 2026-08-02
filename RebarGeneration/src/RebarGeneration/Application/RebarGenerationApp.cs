using System.Reflection;
using Autodesk.Revit.UI;

namespace RebarGeneration.Application;

/// <summary>
/// Внешнее приложение плагина.
/// <para>
/// У него две задачи, и вторая важнее первой. Первая — кнопки на ленте, чтобы
/// заданием мог воспользоваться человек. Вторая — <b>гарантировать, что сборка
/// загружена в процесс Revit</b>: именно поэтому агент может достучаться до
/// <see cref="Api"/> из pyRevit Routes <c>/exec</c> просто по имени типа, без
/// <c>clr.AddReference</c> и без путей. Ровно так уже работают соседние плагины
/// репозитория — это проверено на живом сеансе.
/// </para>
/// </summary>
public sealed class RebarGenerationApp : IExternalApplication
{
    private const string TabName = "Smart Tools";
    private const string PanelName = "Rebar Generation";

    public Result OnStartup(UIControlledApplication app)
    {
        try
        {
            try { app.CreateRibbonTab(TabName); }
            catch { /* вкладку уже создал соседний плагин — это норма */ }

            RibbonPanel panel = app.GetRibbonPanels(TabName)
                                   .FirstOrDefault(p => p.Name == PanelName)
                                ?? app.CreateRibbonPanel(TabName, PanelName);

            string asm = Assembly.GetExecutingAssembly().Location;

            panel.AddItem(new PushButtonData(
                "RebarGenerationRunJob", "Run Job", asm,
                typeof(Commands.RunJobCommand).FullName)
            {
                ToolTip = "Выполнить задание rebar-job.json на текущей модели.",
                LongDescription =
                    "Читает JSON-задание и создаёт арматуру наборами: весь прогон — один шаг "
                    + "отмены, транзакция на хост, повторный запуск по тем же ключам ничего не дублирует.",
            });

            panel.AddItem(new PushButtonData(
                "RebarGenerationDryRun", "Dry Run", asm,
                typeof(Commands.DryRunJobCommand).FullName)
            {
                ToolTip = "Прогнать задание вхолостую: строит всё и откатывает.",
                LongDescription =
                    "Отчёт настоящий (количества, длины, ошибки), модель не меняется. "
                    + "Так проверяют задание перед боевым запуском.",
            });
        }
        catch (Exception ex)
        {
            // Плагин не должен мешать запуску Revit: без ленты Api остаётся
            // полностью работоспособным для агента.
            TaskDialog.Show("RebarGeneration", $"Лента не построена: {ex.Message}");
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
