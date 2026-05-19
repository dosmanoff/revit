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
