using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Engine;
using StairsReinforcement.UI;

namespace StairsReinforcement.Commands;

/// <summary>
/// Phase 4: create a longitudinal section per reinforced stair, plus a rebar schedule and a sheet.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class StairViewsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        var filter = new StairsSelectionFilter();
        List<Element> picked = uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id)).Where(filter.AllowElement).ToList();
        if (picked.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element, filter, "Select reinforced stairs, then click Finish");
                picked = refs.Select(r => doc.GetElement(r.ElementId)).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }
        if (picked.Count == 0)
        {
            TaskDialog.Show("Stair Views", "No stairs or structural floors selected.");
            return Result.Cancelled;
        }

        StairViewsConfig cfg = StairViewsConfigStore.Load(doc);
        var dlg = new StairViewsDialog(cfg);
        if (dlg.ShowDialog() != true) return Result.Cancelled;

        try { StairViewsConfigStore.Save(doc, cfg); } catch { /* best-effort */ }

        List<StairAssembly> assemblies = StairSourceResolver.Resolve(doc, picked);

        ViewRunResult result;
        using (var group = new TransactionGroup(doc, "Stair Views"))
        {
            group.Start();
            result = new StairViewsEngine(doc, cfg).Run(assemblies);
            group.Assimilate();
        }

        ShowResults(result);
        return Result.Succeeded;
    }

    private static void ShowResults(ViewRunResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Section views created: {result.ViewsCreated}");
        if (result.SchedulesCreated > 0) sb.AppendLine($"Schedules: {result.SchedulesCreated}");
        if (result.SheetsCreated > 0) sb.AppendLine($"Sheets: {result.SheetsCreated}");
        if (result.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Issues ({result.Errors.Count}):");
            foreach (string e in result.Errors.Take(10)) sb.AppendLine($"  • {e}");
        }
        TaskDialog.Show("Stair Views", sb.ToString().TrimEnd());
    }
}
