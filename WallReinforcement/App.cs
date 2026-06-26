using Autodesk.Revit.UI;
using System.Reflection;
using WallReinforcement.Commands;

namespace WallReinforcement;

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
            name: "WallReinforcement",
            text: "Wall\nReinforcement",
            assemblyName: assemblyPath,
            className: typeof(WallReinforcementCommand).FullName!);

        buttonData.ToolTip = "Generate rebar on selected monolithic walls.";
        buttonData.LongDescription =
            "Opens a dialog to select a JSON reinforcement config and place " +
            "face-mesh, opening, and edge rebar on the chosen walls. " +
            "Re-running the same config on a wall replaces the prior result.";

        panel.AddItem(buttonData);

        var viewsButtonData = new PushButtonData(
            name: "WallViews",
            text: "Wall\nViews",
            assemblyName: assemblyPath,
            className: typeof(WallViewsCommand).FullName!);

        viewsButtonData.ToolTip = "Create reinforcement views, schedules and sheets for selected walls.";
        viewsButtonData.LongDescription =
            "For each selected wall: exterior/interior face elevations, a horizontal section, an " +
            "optional 3D cage, a rebar schedule and a sheet — each isolated to that wall's rebar.";

        panel.AddItem(viewsButtonData);
    }
}
