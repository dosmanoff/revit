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

        var dialog = new ColumnViewsDialog(config, TitleBlockNames(doc));
        if (dialog.ShowDialog() != true)
            return Result.Cancelled;

        config = dialog.Config;
        ColumnViewsConfigStore.Save(doc, config);

        IList<ElementId> columnIds = GetColumnSelection(uidoc);
        if (columnIds.Count == 0)
            return Result.Cancelled;

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

    /// <summary>
    /// Returns the structural columns in the current selection; if none are selected,
    /// prompts the user to pick them. Returns an empty list when the user cancels.
    /// </summary>
    private static IList<ElementId> GetColumnSelection(UIDocument uidoc)
    {
        var filter = new ColumnSelectionFilter();

        List<ElementId> fromSelection = uidoc.Selection.GetElementIds()
            .Where(id => filter.AllowElement(uidoc.Document.GetElement(id)))
            .ToList();

        if (fromSelection.Count > 0)
            return fromSelection;

        try
        {
            IList<Reference> picked = uidoc.Selection.PickObjects(
                ObjectType.Element, filter, "Select structural columns to document.");
            return picked.Select(r => r.ElementId).ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return [];
        }
    }
}
