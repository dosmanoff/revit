using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WallReinforcement.Config;
using WallReinforcement.Engine;
using WallReinforcement.UI;

namespace WallReinforcement.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class WallReinforcementCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        IList<ElementId> wallIds = GetSelectedWallIds(uidoc);
        if (wallIds.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new WallSelectionFilter(),
                    "Pick structural walls, then press Finish");
                wallIds = refs.Select(r => r.ElementId).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }

        if (wallIds.Count == 0)
        {
            TaskDialog.Show("Wall Reinforcement", "No structural walls selected.");
            return Result.Cancelled;
        }

        string? folder = FolderStorage.Get(doc);
        var dialog = new WallReinforcementDialog(folder, wallIds.Count);
        if (dialog.ShowDialog() != true) return Result.Cancelled;

        // Persist the folder choice on the project for next time.
        if (!string.IsNullOrEmpty(dialog.FolderPath) && dialog.FolderPath != folder)
        {
            using var settingsTx = new Transaction(doc, "Save WR folder");
            settingsTx.Start();
            FolderStorage.Set(doc, dialog.FolderPath);
            settingsTx.Commit();
        }

        ReinforcementConfig cfg = dialog.Config!;

        using var group = new TransactionGroup(doc, $"Wall Reinforcement: {cfg.Name}");
        group.Start();

        var reinforcer = new WallReinforcer(doc);
        Domain.RunResult result = reinforcer.Run(wallIds, cfg, dialog.DryRun);

        if (dialog.DryRun)
            group.RollBack();
        else
            group.Assimilate();

        ResultsDialog.Show(result);
        return Result.Succeeded;
    }

    private static IList<ElementId> GetSelectedWallIds(UIDocument uidoc)
    {
        var filter = new WallSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Where(id => filter.AllowElement(uidoc.Document.GetElement(id)))
            .ToList();
    }
}
