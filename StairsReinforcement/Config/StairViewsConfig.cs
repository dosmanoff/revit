using Autodesk.Revit.DB;

namespace StairsReinforcement.Config;

public enum ViewDuplicateHandling { Skip, Overwrite, AppendSuffix }

/// <summary>
/// Settings for the Stair Views command — a longitudinal section per stair, a rebar schedule and
/// a sheet. Persisted in the document via <see cref="StairViewsConfigStore"/>.
/// </summary>
public sealed class StairViewsConfig
{
    public int SchemaVersion { get; set; } = 1;

    public double CropPadding { get; set; } = 1.0;            // ft
    public int ViewScale { get; set; } = 48;                  // 1/4" = 1'-0"
    public ViewDetailLevel DetailLevel { get; set; } = ViewDetailLevel.Fine;
    public DisplayStyle VisualStyle { get; set; } = DisplayStyle.HLR;

    public string ViewNameTemplate { get; set; } = "{Mark} - Stair Section";
    public string? SectionViewTypeName { get; set; }
    public string? ViewTemplateName { get; set; }

    public bool CreateSchedule { get; set; } = true;
    public string ScheduleNameTemplate { get; set; } = "{Mark} - Rebar Schedule";
    public bool PlaceOnSheet { get; set; } = true;
    public string? TitleBlockName { get; set; }
    public string SheetNumberTemplate { get; set; } = "S-{Mark}";
    public string SheetNameTemplate { get; set; } = "Stair {Mark} Reinforcement";
    public ViewDuplicateHandling DuplicateHandling { get; set; } = ViewDuplicateHandling.AppendSuffix;

    public static StairViewsConfig Default() => new();
}
