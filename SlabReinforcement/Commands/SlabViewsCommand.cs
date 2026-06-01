using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Engine;
using SlabReinforcement.UI;

namespace SlabReinforcement.Commands;

/// <summary>
/// Stage 4: create Layer 1-4 plan views (and, from PR-14, schedules and sheets) for the
/// selected reinforced slabs.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class SlabViewsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        List<Floor> floors = GetSelectedFloors(uidoc);
        if (floors.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element, new SlabSelectionFilter(),
                    "Select reinforced floor slabs, then click Finish");
                floors = refs.Select(r => doc.GetElement(r.ElementId)).OfType<Floor>().ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }
        if (floors.Count == 0)
        {
            TaskDialog.Show("Slab Views", "No floor slabs selected.");
            return Result.Cancelled;
        }

        SlabViewsConfig cfg = SlabViewsConfigStore.Load(doc);
        var dlg = new SlabViewsDialog(cfg);
        if (dlg.ShowDialog() != true) return Result.Cancelled;

        try { SlabViewsConfigStore.Save(doc, cfg); } catch { /* best-effort */ }

        ViewRunResult result;
        using (var group = new TransactionGroup(doc, "Slab Views"))
        {
            group.Start();
            result = new SlabViewsEngine(doc, cfg).Run(floors.Select(f => f.Id).ToList());
            group.Assimilate();
        }

        ShowResults(result);
        return Result.Succeeded;
    }

    private static void ShowResults(ViewRunResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Layer views created: {result.ViewsCreated}");
        if (result.SchedulesCreated > 0) sb.AppendLine($"Schedules: {result.SchedulesCreated}");
        if (result.SheetsCreated > 0) sb.AppendLine($"Sheets: {result.SheetsCreated}");
        if (result.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Issues ({result.Errors.Count}):");
            foreach (string e in result.Errors.Take(10)) sb.AppendLine($"  • {e}");
        }
        TaskDialog.Show("Slab Views", sb.ToString().TrimEnd());
    }

    private static List<Floor> GetSelectedFloors(UIDocument uidoc)
    {
        Document doc = uidoc.Document;
        return uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).OfType<Floor>().ToList();
    }
}
