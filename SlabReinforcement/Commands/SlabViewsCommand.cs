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
        if (floors.Count == 0 && PickFloors(uidoc) is { } first)
            floors = first;

        SlabViewsConfig cfg = SlabViewsConfigStore.Load(doc);
        List<string> titleBlocks = TitleBlockNames(doc);
        List<string> viewTemplates = ViewTemplateNames(doc);

        // Dialog ↔ re-pick loop, like Column Views.
        while (true)
        {
            var dlg = new SlabViewsDialog(cfg, titleBlocks, viewTemplates, floors.Count);
            if (dlg.ShowDialog() != true) return Result.Cancelled;
            if (!dlg.ReselectRequested) break;
            if (PickFloors(uidoc) is { } picked) floors = picked;
        }
        if (floors.Count == 0)
        {
            TaskDialog.Show("Slab Views", "No floor slabs selected.");
            return Result.Cancelled;
        }

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

    /// <summary>Interactive slab pick; null when the user cancels (caller keeps the old set).</summary>
    private static List<Floor>? PickFloors(UIDocument uidoc)
    {
        try
        {
            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new SlabSelectionFilter(),
                "Select reinforced floor slabs, then click Finish");
            return refs.Select(r => uidoc.Document.GetElement(r.ElementId)).OfType<Floor>().ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
    }

    private static List<string> TitleBlockNames(Document doc) =>
        new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>().Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

    private static List<string> ViewTemplateNames(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(View)).Cast<View>()
            .Where(v => v.IsTemplate)
            .Select(v => v.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
}
