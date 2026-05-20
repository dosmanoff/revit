using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace AutoNumbering.Commands;

/// <summary>
/// Accepts physical model elements (walls, floors, beams, columns, rebar, etc.).
/// Excludes views, annotations, and other non-physical categories.
/// </summary>
public class ElementSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        return elem.Category is { CategoryType: CategoryType.Model };
    }

    public bool AllowReference(Reference reference, XYZ position) => false;
}
