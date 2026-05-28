using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace SmartViews.Commands;

/// <summary>
/// Accepts vertical structural columns only. Slanted columns are excluded because
/// their elevation/plan framing is out of scope for the Column Views tool.
/// </summary>
public sealed class ColumnSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        if (elem is not FamilyInstance fi)
            return false;

        if (fi.Category?.Id.Value != (long)BuiltInCategory.OST_StructuralColumns)
            return false;

        return !fi.IsSlantedColumn;
    }

    public bool AllowReference(Reference reference, XYZ position) => false;
}
