using Autodesk.Revit.UI;
using SlabRebar.Commands;
using System.Reflection;

namespace SlabRebar;

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

        RibbonPanel panel = application.CreateRibbonPanel(tabName, "Reinforcement");
        string assemblyPath = Assembly.GetExecutingAssembly().Location;

        var buttonData = new PushButtonData(
            name: "SlabRebar",
            text: "Slab\nRebar",
            assemblyName: assemblyPath,
            className: typeof(SlabRebarCommand).FullName!);

        buttonData.ToolTip = "Classify slab rebar by face (top/bottom) and direction (X/Y).";
        buttonData.LongDescription =
            "Select rebar elements hosted in a slab, then classify each bar as " +
            "bottom-X, bottom-Y, top-X, or top-Y based on its position and orientation. " +
            "The classification label is written to a chosen parameter (e.g. Comments) " +
            "for use in Revit filters and schedules.";

        panel.AddItem(buttonData);
    }
}
