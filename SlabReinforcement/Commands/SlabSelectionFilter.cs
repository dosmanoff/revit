using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace SlabReinforcement.Commands;

/// <summary>
/// Accepts floor slabs — both regular floors (<c>OST_Floors</c>) and slab
/// foundations / mats (<c>OST_StructuralFoundation</c>). Sloped or curved-edge
/// slabs are accepted at selection time; the engine flags unsupported geometry later.
/// </summary>
public class SlabSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        if (elem is not Floor) return false;

        long cat = elem.Category?.Id.Value ?? 0;
        return cat == (long)BuiltInCategory.OST_Floors
            || cat == (long)BuiltInCategory.OST_StructuralFoundation;
    }

    public bool AllowReference(Reference reference, XYZ position) => false;
}
