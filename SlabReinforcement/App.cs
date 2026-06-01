using Autodesk.Revit.UI;
using System.Reflection;
using SlabReinforcement.Commands;

namespace SlabReinforcement;

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

        RibbonPanel panel = application.CreateRibbonPanel(tabName, "Slab Reinforcement");
        string assemblyPath = Assembly.GetExecutingAssembly().Location;

        panel.AddItem(new PushButtonData(
            name: "SlabExport",
            text: "Export\nSlabs",
            assemblyName: assemblyPath,
            className: typeof(ExportSlabsCommand).FullName!)
        {
            ToolTip = "Export a JSON description of selected slabs for the reinforcement agent.",
            LongDescription =
                "Collects geometry, edge adjacency, openings, supports below, available " +
                "bar/hook types and reinforcement hints for the selected floors, and writes " +
                "them to a JSON file consumed by the external AI agent.",
        });

        panel.AddItem(new PushButtonData(
            name: "SlabGenerate",
            text: "Generate\nSlab Rebar",
            assemblyName: assemblyPath,
            className: typeof(GenerateSlabRebarCommand).FullName!)
        {
            ToolTip = "Generate slab rebar from a slab-assignments CSV (settable max bar length).",
            LongDescription =
                "Reads the per-slab assignments CSV produced by the agent and places field " +
                "reinforcement, edge U-bars, opening trim and support-zone bars. Long runs are " +
                "split at the max bar length and lapped. Re-running a config replaces its prior result.",
        });

        panel.AddItem(new PushButtonData(
            name: "SlabViews",
            text: "Slab\nViews",
            assemblyName: assemblyPath,
            className: typeof(SlabViewsCommand).FullName!)
        {
            ToolTip = "Create Layer 1-4 plan views, rebar schedules and sheets for reinforced slabs.",
            LongDescription =
                "Generates four layer plan views (Bottom-X/Y, Top-X/Y), rebar schedules and lays " +
                "them out on sheets for the selected slabs.",
        });
    }
}
