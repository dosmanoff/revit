using Autodesk.Revit.DB;

namespace SlabReinforcement.Config;

public enum LayerIsolation { Hide, Halftone, Show }
public enum ViewDuplicateHandling { Skip, Overwrite, AppendSuffix }

/// <summary>
/// Settings for the Slab Views command — four layer plan views (Layer 1–4), rebar schedules
/// and sheets. Persisted in the document via <see cref="SlabViewsConfigStore"/>.
/// </summary>
public sealed class SlabViewsConfig
{
    public int SchemaVersion { get; set; } = 1;

    public double CropPadding { get; set; } = 1.0;            // ft
    public int PlanScale { get; set; } = 48;                  // 1/4" = 1'-0"
    public ViewDetailLevel DetailLevel { get; set; } = ViewDetailLevel.Fine;
    public DisplayStyle VisualStyle { get; set; } = DisplayStyle.HLR;
    public LayerIsolation Isolation { get; set; } = LayerIsolation.Hide;

    public string LayerViewNameTemplate { get; set; } = "{Mark} - Layer {N} {Layer}";
    public string? PlanViewTypeName { get; set; }
    public string? ViewTemplateName { get; set; }

    // Schedules + sheets (wired in PR-14)
    public bool CreateSchedule { get; set; } = true;
    public string ScheduleNameTemplate { get; set; } = "{Mark} - Rebar Schedule";
    public bool PlaceOnSheet { get; set; } = true;
    public string? TitleBlockName { get; set; }
    public string SheetNumberTemplate { get; set; } = "S-{Mark}";
    public string SheetNameTemplate { get; set; } = "Slab {Mark} Reinforcement";
    public ViewDuplicateHandling DuplicateHandling { get; set; } = ViewDuplicateHandling.AppendSuffix;

    public static SlabViewsConfig Default() => new();
}
