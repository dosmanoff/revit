using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WallReinforcement.Config;
using WallReinforcement.Domain;
using WallReinforcement.Engine;

namespace WallReinforcement.Commands;

/// <summary>
/// Ribbon command: per selected wall, generate reinforcement documentation — face elevations,
/// a thickness section, an optional 3D cage, a rebar schedule and a sheet — via
/// <see cref="WallViewsEngine"/>. Uses the default <see cref="WallViewsConfig"/> for now (a config
/// dialog can be added later, mirroring SlabViews).
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class WallViewsCommand : IExternalCommand
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
                    ObjectType.Element, new WallSelectionFilter(), "Pick walls to document, then press Finish");
                wallIds = refs.Select(r => r.ElementId).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }

        if (wallIds.Count == 0)
        {
            TaskDialog.Show("Wall Views", "No walls selected.");
            return Result.Cancelled;
        }

        WallViewsConfig cfg = WallViewsConfig.Default();

        using var group = new TransactionGroup(doc, "Wall Views");
        group.Start();
        ViewRunResult result = new WallViewsEngine(doc, cfg).Run(wallIds);
        group.Assimilate();

        var msg = new System.Text.StringBuilder();
        msg.AppendLine($"Views created:     {result.ViewsCreated}");
        msg.AppendLine($"Schedules created: {result.SchedulesCreated}");
        msg.AppendLine($"Sheets created:    {result.SheetsCreated}");
        if (result.Errors.Count > 0)
        {
            msg.AppendLine();
            msg.AppendLine($"Issues ({result.Errors.Count}):");
            foreach (string e in result.Errors.Take(15)) msg.AppendLine("  • " + e);
            if (result.Errors.Count > 15) msg.AppendLine($"  …and {result.Errors.Count - 15} more.");
        }
        TaskDialog.Show("Wall Views", msg.ToString());
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
