using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI.Selection;

namespace SlabRebar.Commands;

public class RebarSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => elem is Rebar;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
