using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using AutoNumbering.Engine;
using AutoNumbering.UI;

namespace AutoNumbering.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class AutoNumberingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        var config = new NumberingConfig();
        IList<ElementId> selectedIds = uidoc.Selection.GetElementIds().ToList();

        // Loop allows the dialog to send the user back to Revit for re-selection.
        while (true)
        {
            if (selectedIds.Count == 0)
            {
                try
                {
                    IList<Reference> refs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new ElementSelectionFilter(),
                        "Select elements to number, then press Finish (Tab key or green check)");

                    selectedIds = refs.Select(r => r.ElementId).ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            var dialog = new AutoNumberingDialog(doc, selectedIds, config);
            bool? dlgResult = dialog.ShowDialog();

            if (dialog.NeedReselect)
            {
                selectedIds = [];
                config = dialog.Config;
                continue;
            }

            if (dlgResult != true)
                return Result.Cancelled;

            config = dialog.Config;

            using var tx = new Transaction(doc, "Auto-Numbering");
            tx.Start();

            var engine = new NumberingEngine(doc);
            var (succeeded, failed, errors) = engine.Apply(dialog.Items, config);

            tx.Commit();

            string summary = $"Numbered {succeeded} element(s).";
            if (failed > 0)
                summary += $"\n{failed} element(s) failed:\n" +
                           string.Join("\n", errors.Take(5));
            if (errors.Count > 5)
                summary += $"\n… and {errors.Count - 5} more (see Revit journal).";

            TaskDialog.Show("Auto-Numbering", summary);
            return Result.Succeeded;
        }
    }
}
