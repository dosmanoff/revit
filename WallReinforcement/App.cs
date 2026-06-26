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

        var exportButtonData = new PushButtonData(
            name: "ExportWalls",
            text: "Export\nWalls",
            assemblyName: assemblyPath,
            className: typeof(ExportWallsCommand).FullName!);

        exportButtonData.ToolTip = "Export selected walls to JSON for the reinforcement agent.";
        exportButtonData.LongDescription =
            "Writes a JSON dump of the selected walls (geometry, openings, corner/T junctions, " +
            "cover, available bar/hook types, hints). The agent turns it into a wall brief that " +
            "Wall Reinforcement consumes per-wall.";

        panel.AddItem(exportButtonData);

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

        var aciButtonData = new PushButtonData(
            name: "AciLengths",
            text: "ACI\nLengths",
            assemblyName: assemblyPath,
            className: typeof(AciLengthsCommand).FullName!);

        aciButtonData.ToolTip = "ACI 318-19 development & lap lengths reference calculator.";
        aciButtonData.LongDescription =
            "Enter f'c / fy and read the tension development length ℓd and Class B lap splice ℓst " +
            "for every ASTM bar size — the same numbers used when Anchorage → Use ACI is on.";

        panel.AddItem(aciButtonData);
    }
}
