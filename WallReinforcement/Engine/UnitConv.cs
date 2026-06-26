using Autodesk.Revit.DB;

namespace WallReinforcement.Engine;

/// <summary>Convert between millimetres (UI/config) and Revit internal feet.</summary>
public static class UnitConv
{
    public static double MmToFt(double mm) =>
        UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

    public static double FtToMm(double ft) =>
        UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

    public static double FtToIn(double ft) =>
        UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Inches);
}
