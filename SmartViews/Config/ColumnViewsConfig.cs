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
    /// Distance the top/bottom end-plan cut plane is moved into the column from its
    /// extreme face (ft). The top plan cuts this far below the top face; the bottom plan
    /// this far above the bottom face.
    /// </summary>
    public double PlanCutInset { get; set; } = 0.25;

    /// <summary>Vertical view-depth of each end plan, measured from the cut plane (ft).</summary>
    public double PlanViewDepth { get; set; } = 0.5;

    // ---- Rebar from neighbouring columns ----

    /// <summary>How to treat rebar hosted by a column other than the target column.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ForeignRebarMode ForeignRebar { get; set; } = ForeignRebarMode.Hide;

    // ---- Naming ----

    /// <summary>Tokens: {Mark} {Direction} {Level} {Type}. Direction = Front/Side.</summary>
    public string ElevationNameTemplate { get; set; } = "{Mark} - Elevation {Direction}";

    /// <summary>Tokens: {Mark} {End} {Level} {Type}. End = Top/Bottom.</summary>
    public string PlanNameTemplate { get; set; } = "{Mark} - Plan {End}";

    /// <summary>Tokens: {Mark}. Name of the generated rebar quantity schedule.</summary>
    public string RebarScheduleNameTemplate { get; set; } = "{Mark} - Rebar Schedule";

    /// <summary>Tokens: {Mark}. Name of the generated bending-detail schedule.</summary>
    public string BendingScheduleNameTemplate { get; set; } = "{Mark} - Bending Details";

    // ---- View family types / templates (optional; fall back to first available) ----

    public string? SectionViewTypeName { get; set; }
    public string? PlanViewTypeName { get; set; }
    public string? ElevationViewTemplate { get; set; }
    public string? PlanViewTemplate { get; set; }

    // ---- Schedules ----

    public bool CreateRebarSchedule { get; set; } = true;
    public bool CreateBendingSchedule { get; set; } = true;

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
