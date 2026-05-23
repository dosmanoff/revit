using Autodesk.Revit.UI;
using Serilog;
using System.Reflection;

namespace RevitPlugin;

/// <summary>
/// Точка входа плагина Revit. Реализует <see cref="IExternalApplication"/>.
/// Регистрирует Ribbon WRS — Wall Reinforcement Suite и подписывается на события приложения.
/// Подробности компоновки Ribbon — в <c>docs/UI_FLOWS.md §1</c>.
/// </summary>
public class Application : IExternalApplication
{
    internal const string TabName = "WRS — Wall Reinforcement Suite";

    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WRSPlugin", "logs", "wrsplugin-.log");

    public Result OnStartup(UIControlledApplication application)
    {
        InitializeLogging();
        Log.Information("WRS Plugin OnStartup — Revit {Version}",
            application.ControlledApplication.VersionNumber);

        try
        {
            CreateRibbon(application);
            Log.Information("Ribbon panels created successfully");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize WRS Plugin");
            TaskDialog.Show("WRS Plugin Error",
                $"Failed to load plugin:\n{ex.Message}");
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        Log.Information("WRS Plugin OnShutdown");
        Log.CloseAndFlush();
        return Result.Succeeded;
    }

    // ──────────────────────────────────────────────────────────────────────────

    private static void InitializeLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: _logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void CreateRibbon(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); }
        catch { /* вкладка уже существует */ }

        var dllPath = Assembly.GetExecutingAssembly().Location;
        var wallsPanel = app.CreateRibbonPanel(TabName, "Walls");

        var armWalls = new PushButtonData(
            name: "ArmWalls",
            text: "Arm\nWalls",
            assemblyName: dllPath,
            className: "RevitPlugin.Commands.ArmWallCommand")
        {
            ToolTip = "Разместить арматуру в выбранных стенах по конфигурации.",
            LongDescription = "Использует Wall Link стены или предлагает выбрать конфиг вручную. "
                            + "Запуск идёт в одной транзакции; Undo возвращает в исходное состояние."
        };

        wallsPanel.AddItem(armWalls);

        // Будущие команды Ribbon (см. docs/UI_FLOWS.md §1):
        // Configurations: Config Editor, Open Config Folder
        // Walls: Link Wall, Update Rebar, Modify Rebar, Delete Rebar
        // Dowels: Create Dowels, Update Dowels
        // Output: Create Views, Create Schedules
        // Появятся по мере прохождения этапов ROADMAP M1–M5.
    }
}
