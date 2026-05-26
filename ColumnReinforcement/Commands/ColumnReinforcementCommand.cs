using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;
using ColumnReinforcement.Engine;
using ColumnReinforcement.UI;

namespace ColumnReinforcement.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ColumnReinforcementCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        IList<ElementId> columnIds = GetSelectedColumnIds(uidoc);
        if (columnIds.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new ColumnSelectionFilter(),
                    "Pick structural columns, then press Finish");
                columnIds = refs.Select(r => r.ElementId).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }

        if (columnIds.Count == 0)
        {
            TaskDialog.Show("Column Reinforcement", "No structural columns selected.");
            return Result.Cancelled;
        }

        string? folder = FolderStorage.Get(doc);
        var dialog = new ColumnReinforcementDialog(folder, columnIds.Count);
        if (dialog.ShowDialog() != true) return Result.Cancelled;

        // Persist the folder choice on the project for next time.
        if (!string.IsNullOrEmpty(dialog.FolderPath) && dialog.FolderPath != folder)
        {
            using var settingsTx = new Transaction(doc, "Save Column Reinforcement folder");
            settingsTx.Start();
            FolderStorage.Set(doc, dialog.FolderPath);
            settingsTx.Commit();
        }

        ColumnReinforcementConfig cfg = dialog.Config!;

        using var group = new TransactionGroup(doc, $"Column Reinforcement: {cfg.Name}");
        group.Start();

        var reinforcer = new ColumnReinforcer(doc);
        RunResult result = reinforcer.Run(columnIds, cfg, dialog.DryRun);

        if (dialog.DryRun)
            group.RollBack();
        else
            group.Assimilate();

        ResultsDialog.Show(result);
        return Result.Succeeded;
    }

    private static IList<ElementId> GetSelectedColumnIds(UIDocument uidoc)
    {
        var filter = new ColumnSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Where(id => filter.AllowElement(uidoc.Document.GetElement(id)))
            .ToList();
    }
}
