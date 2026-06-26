using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WallReinforcement.UI;

namespace WallReinforcement.Commands;

/// <summary>
/// Ribbon command: open the standalone ACI 318-19 anchorage reference calculator
/// (<see cref="AciLengthsDialog"/>) — tension development length ℓd and Class B lap splice ℓst per
/// bar size for a given f'c / fy. The same numbers Wall Reinforcement uses with Anchorage → ACI.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class AciLengthsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        new AciLengthsDialog().ShowDialog();
        return Result.Succeeded;
    }
}
