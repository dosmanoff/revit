using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartViews.Config;
using SmartViews.Engine;
using SmartViews.UI;

namespace SmartViews.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class SmartViewsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        // Load persisted config (falls back to defaults when none saved yet).
        ViewConfig config = ConfigLoader.Load(doc);

        var dialog = new SmartViewsDialog(config);
        if (dialog.ShowDialog() != true)
            return Result.Cancelled;

        // Persist updated settings so they survive session restarts.
        ConfigLoader.Save(doc, dialog.Config);

        IList<ElementId> selectedIds = uidoc.Selection.GetElementIds().ToList();
        if (selectedIds.Count == 0)
        {
            TaskDialog.Show("SmartViews", "Select one or more elements before running SmartViews.");
            return Result.Cancelled;
        }

        var engine = new ViewCreationEngine(doc, dialog.Config);

        using var txGroup = new TransactionGroup(doc, "SmartViews — Create Views");
        txGroup.Start();

        ViewCreationResult result = engine.Run(selectedIds);

        if (result.HasErrors && !ConfirmPartialSuccess(result))
        {
            txGroup.RollBack();
            return Result.Cancelled;
        }

        txGroup.Assimilate();

        ShowSummary(result);
        return Result.Succeeded;
    }

    private static bool ConfirmPartialSuccess(ViewCreationResult result)
    {
        var td = new TaskDialog("SmartViews — Errors")
        {
            MainInstruction = $"{result.ErrorCount} error(s) occurred during view creation.",
            MainContent = string.Join("\n", result.Errors.Take(10)),
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.Yes,
        };
        td.MainInstruction += "\n\nCommit the views that succeeded?";
        return td.Show() == TaskDialogResult.Yes;
    }

    private static void ShowSummary(ViewCreationResult result)
    {
        string msg = $"Created {result.CreatedCount} view(s).";
        if (result.SkippedCount > 0)
            msg += $"\nSkipped {result.SkippedCount} duplicate(s).";
        if (result.ErrorCount > 0)
            msg += $"\n{result.ErrorCount} error(s) — see journal for details.";

        TaskDialog.Show("SmartViews", msg);
    }
}
