using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WallReinforcement.Config;
using WallReinforcement.Domain;
using WallReinforcement.Engine;
using WallReinforcement.UI;

namespace WallReinforcement.Commands;

/// <summary>
/// Ribbon command: per selected wall, generate reinforcement documentation — face elevations,
/// a thickness section, an optional 3D cage, a rebar schedule and a sheet — via
/// <see cref="WallViewsEngine"/>. Options come from a <see cref="WallViewsDialog"/> and persist on
/// the document via <see cref="WallViewsConfigStore"/> (mirrors Slab Views).
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

        WallViewsConfig cfg = WallViewsConfigStore.Load(doc);
        List<string> titleBlocks = TitleBlockNames(doc);
        List<string> viewTemplates = ViewTemplateNames(doc);

        // Dialog ↔ re-pick loop, like Slab Views.
        while (true)
        {
            var dlg = new WallViewsDialog(cfg, titleBlocks, viewTemplates, wallIds.Count);
            if (dlg.ShowDialog() != true) return Result.Cancelled;
            if (!dlg.ReselectRequested) break;
            if (PickWalls(uidoc) is { } picked) wallIds = picked;
        }

        if (wallIds.Count == 0)
        {
            TaskDialog.Show("Wall Views", "No walls selected.");
            return Result.Cancelled;
        }

        try { WallViewsConfigStore.Save(doc, cfg); } catch { /* persistence is best-effort */ }

        ViewRunResult result;
        using (var group = new TransactionGroup(doc, "Wall Views"))
        {
            group.Start();
            result = new WallViewsEngine(doc, cfg).Run(wallIds);
            group.Assimilate();
        }

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

    private static IList<ElementId>? PickWalls(UIDocument uidoc)
    {
        try
        {
            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new WallSelectionFilter(), "Pick walls to document, then press Finish");
            return refs.Select(r => r.ElementId).ToList();
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
