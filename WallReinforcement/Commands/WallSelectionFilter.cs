using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI.Selection;

namespace WallReinforcement.Commands;

/// <summary>
/// Accepts structural (Bearing/Shear/Combined) basic walls only.
/// Curtain, stacked, and in-place walls are excluded.
/// </summary>
public class WallSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        if (elem is not Wall wall) return false;
        if (wall.WallType.Kind != WallKind.Basic) return false;

        Parameter? usageParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM);
        if (usageParam is null) return false;

        var usage = (StructuralWallUsage)usageParam.AsInteger();
        return usage is StructuralWallUsage.Bearing
                      or StructuralWallUsage.Shear
                      or StructuralWallUsage.Combined;
    }

    public bool AllowReference(Reference reference, XYZ position) => false;
}
