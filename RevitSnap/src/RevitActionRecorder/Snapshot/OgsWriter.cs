using System.Text.Json;
using Autodesk.Revit.DB;

namespace RevitActionRecorder.Snapshot;

/// <summary>Разворачивание OverrideGraphicSettings во все поля; пишутся только заданные (не-дефолтные).</summary>
internal static class OgsWriter
{
    public static bool IsDefault(OverrideGraphicSettings o)
    {
        try
        {
            return !o.ProjectionLineColor.IsValid
                && o.ProjectionLinePatternId == ElementId.InvalidElementId
                && o.ProjectionLineWeight == OverrideGraphicSettings.InvalidPenNumber
                && !o.CutLineColor.IsValid
                && o.CutLinePatternId == ElementId.InvalidElementId
                && o.CutLineWeight == OverrideGraphicSettings.InvalidPenNumber
                && o.SurfaceForegroundPatternId == ElementId.InvalidElementId
                && !o.SurfaceForegroundPatternColor.IsValid
                && o.IsSurfaceForegroundPatternVisible
                && o.SurfaceBackgroundPatternId == ElementId.InvalidElementId
                && !o.SurfaceBackgroundPatternColor.IsValid
                && o.IsSurfaceBackgroundPatternVisible
                && o.CutForegroundPatternId == ElementId.InvalidElementId
                && !o.CutForegroundPatternColor.IsValid
                && o.IsCutForegroundPatternVisible
                && o.CutBackgroundPatternId == ElementId.InvalidElementId
                && !o.CutBackgroundPatternColor.IsValid
                && o.IsCutBackgroundPatternVisible
                && o.Transparency == 0
                && !o.Halftone
                && o.DetailLevel == ViewDetailLevel.Undefined;
        }
        catch
        {
            return true;
        }
    }

    public static void Write(Utf8JsonWriter w, string propertyName, OverrideGraphicSettings o)
    {
        w.WritePropertyName(propertyName);
        w.WriteStartObject();

        WriteColor(w, "projLineColor", o.ProjectionLineColor);
        WriteId(w, "projLinePattern", o.ProjectionLinePatternId);
        WriteWeight(w, "projLineWeight", o.ProjectionLineWeight);

        WriteColor(w, "cutLineColor", o.CutLineColor);
        WriteId(w, "cutLinePattern", o.CutLinePatternId);
        WriteWeight(w, "cutLineWeight", o.CutLineWeight);

        WriteId(w, "surfForePattern", o.SurfaceForegroundPatternId);
        WriteColor(w, "surfForeColor", o.SurfaceForegroundPatternColor);
        if (!o.IsSurfaceForegroundPatternVisible) w.WriteBoolean("surfForeVisible", false);

        WriteId(w, "surfBackPattern", o.SurfaceBackgroundPatternId);
        WriteColor(w, "surfBackColor", o.SurfaceBackgroundPatternColor);
        if (!o.IsSurfaceBackgroundPatternVisible) w.WriteBoolean("surfBackVisible", false);

        WriteId(w, "cutForePattern", o.CutForegroundPatternId);
        WriteColor(w, "cutForeColor", o.CutForegroundPatternColor);
        if (!o.IsCutForegroundPatternVisible) w.WriteBoolean("cutForeVisible", false);

        WriteId(w, "cutBackPattern", o.CutBackgroundPatternId);
        WriteColor(w, "cutBackColor", o.CutBackgroundPatternColor);
        if (!o.IsCutBackgroundPatternVisible) w.WriteBoolean("cutBackVisible", false);

        if (o.Transparency != 0) w.WriteNumber("transparency", o.Transparency);
        if (o.Halftone) w.WriteBoolean("halftone", true);
        if (o.DetailLevel != ViewDetailLevel.Undefined) w.WriteString("detailLevel", o.DetailLevel.ToString());

        w.WriteEndObject();
    }

    private static void WriteColor(Utf8JsonWriter w, string name, Color color)
    {
        if (color is null || !color.IsValid) return;
        w.WritePropertyName(name);
        w.WriteStartArray();
        w.WriteNumberValue(color.Red);
        w.WriteNumberValue(color.Green);
        w.WriteNumberValue(color.Blue);
        w.WriteEndArray();
    }

    private static void WriteId(Utf8JsonWriter w, string name, ElementId id)
    {
        if (id is null || id == ElementId.InvalidElementId) return;
        w.WriteNumber(name, id.Value);
    }

    private static void WriteWeight(Utf8JsonWriter w, string name, int weight)
    {
        if (weight == OverrideGraphicSettings.InvalidPenNumber) return;
        w.WriteNumber(name, weight);
    }
}
