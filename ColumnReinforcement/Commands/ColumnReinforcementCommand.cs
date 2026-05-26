using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ColumnReinforcement.Config;
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

        // Engine arrives in PR-04. For now confirm the round-trip works and report the chosen config.
        TaskDialog.Show(
            "Column Reinforcement",
            $"Ready to reinforce {columnIds.Count} column(s) with config '{cfg.Name}'.\n\n" +
            $"  Units:        {cfg.Units}\n" +
            $"  Longitudinal: {cfg.Longitudinal.BarType} ({cfg.Longitudinal.BarsAlongWidth}×{cfg.Longitudinal.BarsAlongDepth}{(cfg.Longitudinal.CornerOnly ? ", corners only" : "")})\n" +
            $"  Ties:         {(cfg.Stirrups.Enabled ? $"{cfg.Stirrups.BarType} @ {cfg.Stirrups.Spacing}" : "disabled")}\n" +
            $"  Dry run:      {dialog.DryRun}\n\n" +
            "Engine arrives in PR-04 (longitudinal bars) and PR-05 (ties).");

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
