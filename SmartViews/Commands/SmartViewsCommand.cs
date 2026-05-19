using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartViews.Config;
using SmartViews.Engine;
using SmartViews.UI;

namespace SmartViews.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class SmartViewsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        ViewConfig config = ConfigLoader.Load(doc);

        var dialog = new SmartViewsDialog(config);
        if (dialog.ShowDialog() != true)
            return Result.Cancelled;

        ConfigLoader.Save(doc, dialog.Config);

        IList<ElementId> selectedIds = uidoc.Selection.GetElementIds().ToList();
        if (selectedIds.Count == 0)
        {
            TaskDialog.Show("SmartViews", "Select one or more elements before running SmartViews.");
            return Result.Cancelled;
        }

        var engine = new ViewCreationEngine(doc, dialog.Config);

        using var txGroup = new TransactionGroup(doc, "SmartViews — Create Views");
        txGroup.Start();

        ViewCreationResult result = engine.Run(selectedIds);

        var summary = new ErrorSummaryDialog(result);
        if (summary.ShowDialog() != true)
        {
            txGroup.RollBack();
            return Result.Cancelled;
        }

        txGroup.Assimilate();
        return Result.Succeeded;
    }
}
