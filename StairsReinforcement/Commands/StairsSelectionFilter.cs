using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI.Selection;

namespace StairsReinforcement.Commands;

/// <summary>
/// Allows the two supported stair representations to be picked:
/// native Revit <see cref="Stairs"/> elements, and structural <see cref="Floor"/>s
/// (sloped flights / flat landings modelled as floors or foundations).
/// </summary>
public class StairsSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        if (elem is Stairs) return true;
        if (elem is Floor)
        {
            int cat = elem.Category?.Id.Value is long v ? (int)v : 0;
            return cat == (int)BuiltInCategory.OST_Floors
                || cat == (int)BuiltInCategory.OST_StructuralFoundation;
        }
        return false;
    }

    public bool AllowReference(Reference reference, XYZ position) => false;
}
