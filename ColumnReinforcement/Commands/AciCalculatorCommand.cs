using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ColumnReinforcement.UI;

namespace ColumnReinforcement.Commands;

/// <summary>
/// Opens the ACI 318 anchorage calculator. Read-only — never touches the model.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
[Regeneration(RegenerationOption.Manual)]
public class AciCalculatorCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var dlg = new AciAnchorageCalculatorDialog();
        dlg.ShowDialog();
        return Result.Succeeded;
    }
}
