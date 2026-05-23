using Autodesk.Revit.UI;
using Serilog;
using System.Reflection;

namespace RevitPlugin;

/// <summary>
/// Точка входа плагина Revit. Реализует <see cref="IExternalApplication"/>.
/// Регистрирует UI (Ribbon) и подписывается на события приложения.
/// </summary>
public class Application : IExternalApplication
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RevitPlugin", "logs", "revitplugin-.log");

    /// <summary>
    /// Вызывается при запуске Revit. Инициализирует логирование и создаёт UI.
    /// </summary>
    public Result OnStartup(UIControlledApplication application)
    {
        InitializeLogging();
        Log.Information("RevitPlugin OnStartup — Revit {Version}",
            application.ControlledApplication.VersionNumber);

        try
        {
            CreateRibbonPanel(application);
            Log.Information("Ribbon panel created successfully");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize RevitPlugin");
            TaskDialog.Show("RevitPlugin Error",
                $"Failed to load plugin:\n{ex.Message}");
            return Result.Failed;
        }
    }

    /// <summary>
    /// Вызывается при закрытии Revit. Освобождает ресурсы и закрывает логи.
    /// </summary>
    public Result OnShutdown(UIControlledApplication application)
    {
        Log.Information("RevitPlugin OnShutdown");
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

    private static void CreateRibbonPanel(UIControlledApplication app)
    {
        // Создать вкладку
        const string tabName = "BIM Tools";
        try { app.CreateRibbonTab(tabName); }
        catch { /* вкладка уже существует */ }

        // Создать панель
        var panel = app.CreateRibbonPanel(tabName, "Automation");

        // Путь к DLL плагина
        var dllPath = Assembly.GetExecutingAssembly().Location;

        // Кнопка примера команды
        var btnData = new PushButtonData(
            name: "CreateWalls",
            text: "Create Walls",
            assemblyName: dllPath,
            className: "RevitPlugin.Commands.CreateWallsCommand")
        {
            ToolTip = "Creates walls from selected lines",
            LongDescription = "Select model lines and click to create walls along them.",
        };

        panel.AddItem(btnData);
    }
}
