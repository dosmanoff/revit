using Autodesk.Revit.UI;
using ColumnReinforcement.Commands;
using System.Reflection;

namespace ColumnReinforcement;

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
            name: "ColumnReinforcement",
            text: "Column\nReinforcement",
            assemblyName: assemblyPath,
            className: typeof(ColumnReinforcementCommand).FullName!);

        buttonData.ToolTip = "Generate rebar on selected reinforced-concrete columns.";
        buttonData.LongDescription =
            "Opens a dialog to select a JSON reinforcement config and place " +
            "longitudinal bars and ties on the chosen columns. " +
            "Re-running the same config on a column replaces the prior result.";

        panel.AddItem(buttonData);
    }
}
