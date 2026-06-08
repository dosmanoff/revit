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

    // Cross-sections through the slab (like ColumnViews' elevations) — two cuts (each way),
    // through the slab centroid, with the SR rebar shown unobscured.
    public bool CreateSections { get; set; } = true;
    public string? SectionViewTypeName { get; set; }
    public double SectionDepthFt { get; set; } = 2.0;        // view depth past the slab footprint
    public string SectionNameTemplate { get; set; } = "{Mark} - Section {Dir}";

    // 3D isolated cage + bending-detail "details" (like ColumnViews)
    public bool Create3DView { get; set; } = true;
    public int View3DScale { get; set; } = 24;
    public string View3DNameTemplate { get; set; } = "{Mark} - 3D Cage";
    public bool CreateBendingDetails { get; set; } = true;
    public int BendingDetailScale { get; set; } = 12;
    public string BendingDetailNameTemplate { get; set; } = "{Mark} - Bending Details";

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
