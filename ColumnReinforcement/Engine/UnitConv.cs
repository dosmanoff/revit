using Autodesk.Revit.DB;

namespace ColumnReinforcement.Engine;

/// <summary>Convert between Imperial / Metric UI units and Revit internal feet.</summary>
public static class UnitConv
{
    public static double InToFt(double inches) =>
        UnitUtils.ConvertToInternalUnits(inches, UnitTypeId.Inches);

    public static double FtToIn(double ft) =>
        UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Inches);

    public static double MmToFt(double mm) =>
        UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

    public static double FtToMm(double ft) =>
        UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
}
