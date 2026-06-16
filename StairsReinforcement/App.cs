using Autodesk.Revit.UI;
using System.Reflection;
using StairsReinforcement.Commands;

namespace StairsReinforcement;

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

        // Shared tab — created idempotently (Revit throws if it already exists).
        try { application.CreateRibbonTab(tabName); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { }

        RibbonPanel panel = application.CreateRibbonPanel(tabName, "Stairs Reinforcement");
        string assemblyPath = Assembly.GetExecutingAssembly().Location;

        panel.AddItem(new PushButtonData(
            name: "StairsExport",
            text: "Export\nStairs",
            assemblyName: assemblyPath,
            className: typeof(ExportStairsCommand).FullName!)
        {
            ToolTip = "Export a JSON description of selected stairs for the reinforcement agent.",
            LongDescription =
                "Collects per-flight and per-landing geometry (waist, slope, width, run/rise, " +
                "supports at each end), openings, available bar/hook types and reinforcement hints " +
                "for the selected stairs (native Stairs or floor-modelled), and writes them to a " +
                "JSON file consumed by the external AI agent.",
        });

        panel.AddItem(new PushButtonData(
            name: "StairsGenerate",
            text: "Generate\nStair Rebar",
            assemblyName: assemblyPath,
            className: typeof(GenerateStairsRebarCommand).FullName!)
        {
            ToolTip = "Generate stair rebar from a stair-assignments CSV or a single config.",
            LongDescription =
                "Reads the per-stair assignments produced by the agent and places flight " +
                "longitudinal + distribution bars, landing mats, knee/transition bars and starter " +
                "dowels. Each bar set is independently configurable. Re-running a config replaces " +
                "its prior result.",
        });

        panel.AddItem(new PushButtonData(
            name: "StairViews",
            text: "Stair\nViews",
            assemblyName: assemblyPath,
            className: typeof(StairViewsCommand).FullName!)
        {
            ToolTip = "Create a section view, rebar schedule and sheet for the selected reinforced stairs.",
            LongDescription =
                "Generates a longitudinal section per stair (oriented along the run), a rebar " +
                "schedule filtered to the stair, and lays them out on a sheet.",
        });
    }
}
