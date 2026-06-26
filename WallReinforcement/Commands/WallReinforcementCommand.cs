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

        var reinforcer = new WallReinforcer(doc);
        var skipped = new List<string>();
        Domain.RunResult result;

        string groupName = dialog.UseBrief ? "Wall Reinforcement: brief" : $"Wall Reinforcement: {dialog.Config!.Name}";
        using (var group = new TransactionGroup(doc, groupName))
        {
            group.Start();

            if (dialog.UseBrief)
            {
                Dictionary<ElementId, ReinforcementConfig>? perWall = ResolveBrief(doc, wallIds, dialog.BriefPath!, skipped, out string? err);
                if (perWall is null)
                {
                    group.RollBack();
                    TaskDialog.Show("Wall Reinforcement", err!);
                    return Result.Failed;
                }
                if (perWall.Count == 0)
                {
                    group.RollBack();
                    TaskDialog.Show("Wall Reinforcement",
                        "No selected wall matched a brief entry.\n\n" + string.Join("\n", skipped));
                    return Result.Cancelled;
                }
                result = reinforcer.Run(perWall, dialog.DryRun);
            }
            else
            {
                result = reinforcer.Run(wallIds, dialog.Config!, dialog.DryRun);
            }

            if (dialog.DryRun)
                group.RollBack();
            else
                group.Assimilate();
        }

        ResultsDialog.Show(result, skipped);
        return Result.Succeeded;
    }

    /// <summary>
    /// Load the brief and match each selected wall to an entry by Mark / Id, mapping it to a
    /// per-wall config. Returns null (with <paramref name="error"/> set) if the brief can't be read.
    /// </summary>
    private static Dictionary<ElementId, ReinforcementConfig>? ResolveBrief(
        Document doc, IList<ElementId> wallIds, string briefPath, List<string> skipped, out string? error)
    {
        error = null;
        WallBrief brief;
        try
        {
            brief = BriefLoader.Load(briefPath);
        }
        catch (Exception ex)
        {
            error = $"Could not load brief:\n{ex.Message}";
            return null;
        }

        var perWall = new Dictionary<ElementId, ReinforcementConfig>();
        foreach (ElementId id in wallIds)
        {
            if (doc.GetElement(id) is not Wall w) continue;
            string? mark = w.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            mark = string.IsNullOrWhiteSpace(mark) ? null : mark;
            BriefWall? bw = BriefLoader.Match(brief, mark, id.Value);
            if (bw is null)
            {
                skipped.Add($"Wall {id.Value} (Mark '{mark ?? "(none)"}'): no brief entry.");
                continue;
            }
            perWall[id] = BriefMapper.ToConfig(brief, bw);
        }
        return perWall;
    }

    private static IList<ElementId> GetSelectedWallIds(UIDocument uidoc)
    {
        var filter = new WallSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Where(id => filter.AllowElement(uidoc.Document.GetElement(id)))
            .ToList();
    }
}
