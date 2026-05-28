using Autodesk.Revit.UI;
using SmartViews.Commands;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace SmartViews;

public class App : IExternalApplication
{
    internal static App? Instance { get; private set; }

    public Result OnStartup(UIControlledApplication application)
    {
        Instance = this;

        CreateRibbonPanel(application);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        Instance = null;
        return Result.Succeeded;
    }

    private static void CreateRibbonPanel(UIControlledApplication application)
    {
        const string tabName = "SmartViews";

        try
        {
            application.CreateRibbonTab(tabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Tab already exists — safe to continue.
        }

        RibbonPanel panel = application.CreateRibbonPanel(tabName, "Views");

        string assemblyPath = Assembly.GetExecutingAssembly().Location;

        var buttonData = new PushButtonData(
            name: "CreateSmartViews",
            text: "Create\nViews",
            assemblyName: assemblyPath,
            className: typeof(SmartViewsCommand).FullName!);

        buttonData.ToolTip = "Batch-create sections, elevations, and plans from selected elements.";
        buttonData.LongDescription =
            "Opens the SmartViews dialog to configure and generate views " +
            "for each selected element based on a saved view configuration.";

        // Optional icon — 32×32 px PNG embedded as a resource.
        buttonData.LargeImage = LoadIcon("SmartViews.Resources.icon_32.png");
        buttonData.Image = LoadIcon("SmartViews.Resources.icon_16.png");

        panel.AddItem(buttonData);

        CreateColumnViewsButton(application, assemblyPath);
    }

    /// <summary>
    /// Adds the "Column Views" button to the shared "Smart Tools" tab (the same tab the
    /// reinforcement tools live on), separate from the "SmartViews" tab above.
    /// </summary>
    private static void CreateColumnViewsButton(UIControlledApplication application, string assemblyPath)
    {
        const string tabName = "Smart Tools";

        try
        {
            application.CreateRibbonTab(tabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Tab already created by another add-in — safe to continue.
        }

        RibbonPanel panel = GetOrCreatePanel(application, tabName, "Columns");

        var buttonData = new PushButtonData(
            name: "ColumnViews",
            text: "Column\nViews",
            assemblyName: assemblyPath,
            className: typeof(ColumnViewsCommand).FullName!);

        buttonData.ToolTip = "Generate elevations, end plans, and rebar schedules for selected columns.";
        buttonData.LongDescription =
            "For each selected structural column, creates two perpendicular elevations and two " +
            "end plans (top/bottom), hides or half-tones rebar hosted by other columns, and " +
            "(optionally) builds rebar/bending schedules and a sheet.";
        buttonData.LargeImage = LoadIcon("SmartViews.Resources.icon_32.png");
        buttonData.Image = LoadIcon("SmartViews.Resources.icon_16.png");

        panel.AddItem(buttonData);
    }

    private static RibbonPanel GetOrCreatePanel(
        UIControlledApplication application, string tabName, string panelName)
    {
        foreach (RibbonPanel existing in application.GetRibbonPanels(tabName))
        {
            if (string.Equals(existing.Name, panelName, StringComparison.Ordinal))
                return existing;
        }

        return application.CreateRibbonPanel(tabName, panelName);
    }

    private static BitmapImage? LoadIcon(string resourcePath)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourcePath);

            if (stream is null)
                return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
