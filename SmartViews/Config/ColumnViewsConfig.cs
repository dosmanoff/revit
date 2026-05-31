using Autodesk.Revit.DB;
using System.Text.Json.Serialization;

namespace SmartViews.Config;

/// <summary>
/// Configuration for the Column Views tool: per selected structural column it
/// generates two elevations, two end plans (top/bottom), rebar/bending schedules,
/// and (optionally) a sheet. All linear values are in feet (Revit internal units).
/// </summary>
public sealed class ColumnViewsConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // ---- Crop / depth ----

    /// <summary>Padding added around the column section on every cropped view (ft).</summary>
    public double CropPadding { get; set; } = 0.5;

    /// <summary>
    /// Half-depth of the elevation section in front of / behind the column face (ft).
    /// Small by design so adjacent geometry is excluded — "elevation with shallow depth".
    /// </summary>
    public double ElevationDepth { get; set; } = 0.5;

    /// <summary>
    /// Offset above the relevant stirrup the end-plan cut plane sits at (ft). The top plan
    /// cuts this far above the topmost stirrup; the bottom plan this far above the bottommost
    /// stirrup, so each plan slices through reinforced section rather than the bare end.
    /// </summary>
    public double PlanCutAboveStirrup { get; set; } = 0.1;

    /// <summary>
    /// Fallback cut inset from the column's extreme face (ft), used only when no stirrups are
    /// found hosted by the column.
    /// </summary>
    public double PlanCutInset { get; set; } = 0.25;

    /// <summary>Vertical view-depth of each end plan, measured down from the cut plane (ft).</summary>
    public double PlanViewDepth { get; set; } = 0.5;

    // ---- Rebar from neighbouring columns ----

    /// <summary>How to treat rebar hosted by a column other than the target column.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ForeignRebarMode ForeignRebar { get; set; } = ForeignRebarMode.Hide;

    // ---- View appearance (applied to every generated graphical view) ----

    /// <summary>Scale denominator for elevations. 12 = 1"=1'-0", 48 = 1/4"=1'-0", etc.</summary>
    public int ElevationScale { get; set; } = 12;

    /// <summary>Scale denominator for end plans.</summary>
    public int PlanScale { get; set; } = 12;

    /// <summary>Scale denominator for the 3D view.</summary>
    public int View3DScale { get; set; } = 24;

    /// <summary>Scale denominator for the bending-detail drafting view.</summary>
    public int BendingDetailScale { get; set; } = 12;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ViewDetailLevel DetailLevel { get; set; } = ViewDetailLevel.Fine;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DisplayStyle VisualStyle { get; set; } = DisplayStyle.HLR;

    // ---- Naming ----

    /// <summary>Tokens: {Mark} {Direction} {Level} {Type}. Direction = Front/Side.</summary>
    public string ElevationNameTemplate { get; set; } = "{Mark} - Elevation {Direction}";

    /// <summary>Tokens: {Mark} {End} {Level} {Type}. End = Top/Bottom.</summary>
    public string PlanNameTemplate { get; set; } = "{Mark} - Plan {End}";

    /// <summary>Tokens: {Mark}. Name of the generated rebar schedule.</summary>
    public string RebarScheduleNameTemplate { get; set; } = "{Mark} - Rebar Schedule";

    // ---- View family types / templates (optional; fall back to first available) ----

    public string? SectionViewTypeName { get; set; }
    public string? PlanViewTypeName { get; set; }
    public string? ElevationViewTemplate { get; set; }
    public string? PlanViewTemplate { get; set; }

    // ---- Schedules ----

    public bool CreateRebarSchedule { get; set; } = true;

    /// <summary>
    /// Name of an existing rebar ViewSchedule to use as a template. When set, the per-column
    /// schedule is created by duplicating that schedule and applying a Host Mark filter,
    /// preserving the template's fields/grouping/formatting. Null = build from scratch.
    /// </summary>
    public string? ScheduleTemplateName { get; set; }

    /// <summary>
    /// When true, also creates an isometric 3D view (default orientation) per column showing
    /// only that column and its own rebar, and places it on the sheet.
    /// </summary>
    public bool Create3DView { get; set; } = true;

    /// <summary>Tokens: {Mark} {Level}. Name of the generated 3D view.</summary>
    public string View3DNameTemplate { get; set; } = "{Mark} - 3D";

    /// <summary>
    /// When true, the schedule includes the rebar Shape Image column (the bend-shape diagram),
    /// i.e. "generate bending-detail graphics". When false, only the tabular fields are shown.
    /// </summary>
    public bool BendingDetailGraphics { get; set; } = true;

    /// <summary>
    /// When true, creates a drafting view per column containing a RebarBendingDetail annotation
    /// for each unique bending shape hosted by the column, using Revit's native bending-detail
    /// functionality. The view is placed on the column's sheet.
    /// </summary>
    public bool CreateBendingDetailView { get; set; } = true;

    /// <summary>Tokens: {Mark}. Name of the bending-detail drafting view.</summary>
    public string BendingDetailViewNameTemplate { get; set; } = "{Mark} - Bending Details";

    // ---- Sheet auto-placement ----

    public bool PlaceOnSheet { get; set; } = true;

    /// <summary>Title block family-type name for newly created sheets. Null = first available.</summary>
    public string? TitleBlockName { get; set; }

    /// <summary>Tokens: {Mark} {Level}. Sheet number used when creating a new sheet.</summary>
    public string SheetNumberTemplate { get; set; } = "C-{Mark}";

    /// <summary>Tokens: {Mark} {Level}. Sheet name used when creating a new sheet.</summary>
    public string SheetNameTemplate { get; set; } = "Column {Mark} Reinforcement";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.AppendSuffix;

    public static ColumnViewsConfig Default() => new();
}

/// <summary>Treatment of rebar hosted by columns other than the one being documented.</summary>
public enum ForeignRebarMode
{
    /// <summary>Hide foreign rebar entirely (default).</summary>
    Hide,

    /// <summary>Show foreign rebar in halftone.</summary>
    Halftone,

    /// <summary>Leave foreign rebar fully visible.</summary>
    Show,
}
