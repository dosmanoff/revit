using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace ColumnReinforcement.Commands;

/// <summary>
/// Accepts vertical structural reinforced-concrete columns only.
/// Slanted columns, architectural columns, and steel columns are excluded.
/// </summary>
public class ColumnSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        if (elem is not FamilyInstance fi) return false;

        BuiltInCategory cat = (BuiltInCategory)fi.Category.Id.Value;
        if (cat != BuiltInCategory.OST_StructuralColumns) return false;

        // Slanted columns have a non-vertical analytical curve. Phase 1: vertical only.
        if (fi.IsSlantedColumn) return false;

        return true;
    }

    public bool AllowReference(Reference reference, XYZ position) => false;
}
