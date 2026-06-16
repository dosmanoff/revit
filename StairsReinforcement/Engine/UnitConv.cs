using Autodesk.Revit.DB;

namespace StairsReinforcement.Engine;

/// <summary>Thin wrappers over <see cref="UnitUtils"/> for the units the dump/engine use.</summary>
public static class UnitConv
{
    public static double FtToIn(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Inches);
    public static double InToFt(double inch) => UnitUtils.ConvertToInternalUnits(inch, UnitTypeId.Inches);
    public static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
    public static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
    public static double Deg(double radians) => radians * 180.0 / Math.PI;
}
