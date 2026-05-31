using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SmartViews.Config;
using SmartViews.Engine;
using SmartViews.UI;

namespace SmartViews.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ColumnViewsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        ColumnViewsConfig config = ColumnViewsConfigStore.Load(doc);
        IReadOnlyList<string> titleBlocks = TitleBlockNames(doc);
        IReadOnlyList<string> scheduleTemplates = RebarScheduleNames(doc);

        // Start from whatever columns are already selected; the dialog lets the user re-pick.
        IList<ElementId> columnIds = ColumnsInSelection(uidoc);

        while (true)
        {
            var dialog = new ColumnViewsDialog(config, titleBlocks, scheduleTemplates, columnIds.Count);
            if (dialog.ShowDialog() != true)
                return Result.Cancelled;

            config = dialog.Config;

            if (!dialog.ReselectRequested)
                break;

            IList<ElementId> picked = PickColumns(uidoc);
            if (picked.Count > 0)
                columnIds = picked;
        }

        if (columnIds.Count == 0)
            return Result.Cancelled;

        ColumnViewsConfigStore.Save(doc, config);

        var engine = new ColumnViewsEngine(doc, config);

        using var group = new TransactionGroup(doc, "Column Views");
        group.Start();

        ViewCreationResult result = engine.Run(columnIds);

        var summary = new ErrorSummaryDialog(result);
        if (summary.ShowDialog() != true)
        {
            group.RollBack();
            return Result.Cancelled;
        }

        group.Assimilate();
        return Result.Succeeded;
    }

    private static IReadOnlyList<string> TitleBlockNames(Document doc) =>
        new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> RebarScheduleNames(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(s => !s.IsTemplate
                && s.Definition?.CategoryId.Value == (long)BuiltInCategory.OST_Rebar)
            .Select(s => s.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Structural columns already selected in the document (no prompt).</summary>
    private static IList<ElementId> ColumnsInSelection(UIDocument uidoc)
    {
        var filter = new ColumnSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Where(id => filter.AllowElement(uidoc.Document.GetElement(id)))
            .ToList();
    }

    /// <summary>Prompts the user to pick structural columns. Empty list when cancelled.</summary>
    private static IList<ElementId> PickColumns(UIDocument uidoc)
    {
        try
        {
            IList<Reference> picked = uidoc.Selection.PickObjects(
                ObjectType.Element, new ColumnSelectionFilter(), "Select structural columns to document.");
            return picked.Select(r => r.ElementId).ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return [];
        }
    }
}
