using Autodesk.Revit.UI;
using AutoNumbering.Commands;
using System.Reflection;

namespace AutoNumbering;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        CreateRibbonPanel(application);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    private static void CreateRibbonPanel(UIControlledApplication application)
    {
        const string tabName = "Smart Tools";

        try { application.CreateRibbonTab(tabName); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { }

        RibbonPanel panel = application.CreateRibbonPanel(tabName, "Numbering");
        string assemblyPath = Assembly.GetExecutingAssembly().Location;

        var buttonData = new PushButtonData(
            name: "AutoNumbering",
            text: "Auto\nNumbering",
            assemblyName: assemblyPath,
            className: typeof(AutoNumberingCommand).FullName!);

        buttonData.ToolTip = "Assign sequential numbers to selected elements.";
        buttonData.LongDescription =
            "Opens a dialog to configure prefix, suffix, and sort order, " +
            "then writes sequential numbers to a chosen parameter (e.g. Mark).";

        panel.AddItem(buttonData);
    }
}
