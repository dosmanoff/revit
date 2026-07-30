using Autodesk.Revit.UI;

namespace RevitActionRecorder.Application;

internal static class RibbonBuilder
{
    // Общая вкладка семейства плагинов SmartTools; панель — своя, в стиле соседних
    // ("Wall Reinforcement", "Stairs Reinforcement").
    private const string TabName = "Smart Tools";
    private const string PanelName = "Action Recorder";

    public static void Build(UIControlledApplication application)
    {
        try { application.CreateRibbonTab(TabName); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { }

        var panel = GetOrCreatePanel(application, TabName, PanelName);
        var assemblyPath = typeof(RibbonBuilder).Assembly.Location;

        AddButton(panel, assemblyPath,
            "RAR_StartRecording", "Start\nrecording",
            typeof(StartRecordingCommand), typeof(StartRecordingAvailability),
            "Начать запись сессии: снапшот «до», PNG эталонных видов, подписка на события Revit.");

        AddButton(panel, assemblyPath,
            "RAR_StopRecording", "Stop\nrecording",
            typeof(StopRecordingCommand), typeof(RecordingActiveAvailability),
            "Остановить запись: снапшот «после», PNG, дифф, итоговая статистика сессии.");

        panel.AddSeparator();

        AddButton(panel, assemblyPath,
            "RAR_SnapshotNow", "Snapshot\nnow",
            typeof(SnapshotNowCommand), typeof(RecordingActiveAvailability),
            "Промежуточный полный снапшот модели без остановки записи.");

        AddButton(panel, assemblyPath,
            "RAR_MarkMoment", "Mark\nmoment",
            typeof(MarkMomentCommand), typeof(RecordingActiveAvailability),
            "Отметить важный момент: комментарий + PNG активного вида отдельным событием в потоке.");

        panel.AddSeparator();

        AddButton(panel, assemblyPath,
            "RAR_OpenSessionFolder", "Open session\nfolder",
            typeof(OpenSessionFolderCommand), typeof(AlwaysAvailable),
            "Открыть папку текущей (или последней) сессии записи.");

        AddButton(panel, assemblyPath,
            "RAR_Settings", "Settings",
            typeof(SettingsCommand), typeof(AlwaysAvailable),
            "Настройки: папка вывода, виды для PNG, пороги, gzip, хеш геометрии, логирование.");
    }

    private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string tabName, string panelName)
    {
        foreach (RibbonPanel existing in application.GetRibbonPanels(tabName))
            if (existing.Name == panelName) return existing;
        return application.CreateRibbonPanel(tabName, panelName);
    }

    private static void AddButton(
        RibbonPanel panel,
        string assemblyPath,
        string internalName,
        string text,
        Type commandType,
        Type availabilityType,
        string tooltip)
    {
        var data = new PushButtonData(internalName, text, assemblyPath, commandType.FullName)
        {
            ToolTip = tooltip,
            AvailabilityClassName = availabilityType.FullName,
        };
        panel.AddItem(data);
    }
}
