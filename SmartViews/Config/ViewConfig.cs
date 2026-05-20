using System.Text.Json.Serialization;

namespace SmartViews.Config;

public sealed class ViewConfig
{
    /// <summary>Folder path where JSON config files are stored.</summary>
    public string ConfigFolderPath { get; set; } = string.Empty;

    /// <summary>Uniform offset (in feet) added around each element bounding box.</summary>
    public double CropOffset { get; set; } = 1.0;

    /// <summary>What to do when a view with the resolved name already exists.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.Skip;

    /// <summary>
    /// View-range offsets applied to every Plan view created in this config.
    /// All values are in feet, measured above the host Level.
    /// Null = use Revit's default view range.
    /// </summary>
    public PlanViewRangeConfig? PlanViewRange { get; set; }

    /// <summary>One entry per view type to create for each selected element.</summary>
    public List<ViewKindConfig> ViewKinds { get; set; } = [];

    public static ViewConfig Default() => new()
    {
        CropOffset = 1.0,
        DuplicateHandling = DuplicateHandling.Skip,
        ViewKinds =
        [
            new ViewKindConfig
            {
                Kind = ViewKind.Section,
                NameTemplate = "{Mark} - {Direction}",
                CreateAllDirections = false,
            },
        ],
    };
}

public sealed class PlanViewRangeConfig
{
    /// <summary>Top clip plane offset above the host level (ft).</summary>
    public double TopOffset    { get; set; } = 7.5;

    /// <summary>Cut plane offset above the host level (ft). Typical: 4 ft.</summary>
    public double CutOffset    { get; set; } = 4.0;

    /// <summary>Bottom clip plane offset above the host level (ft).</summary>
    public double BottomOffset { get; set; } = 0.0;

    /// <summary>View depth plane offset above the host level (ft).</summary>
    public double ViewDepth    { get; set; } = 0.0;
}

public sealed class ViewKindConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ViewKind Kind { get; set; } = ViewKind.Section;

    /// <summary>
    /// Supports tokens: {Mark}, {Level}, {Type}, {Index}, {Direction}.
    /// Example: "{Mark} - {Direction} Elevation"
    /// </summary>
    public string NameTemplate { get; set; } = "{Mark} - {Type}";

    /// <summary>Optional: pin the view to this ViewFamilyType name. Null = first match.</summary>
    public string? ViewFamilyTypeName { get; set; }

    /// <summary>
    /// Direction the viewer stands. Used when CreateAllDirections = false.
    /// When AlignToElement = true, directions are relative to the element's local axes.
    /// Only applies when Kind = Section.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SectionDirection SectionDirection { get; set; } = SectionDirection.South;

    /// <summary>
    /// When true (and Kind = Section), creates four views — one for each SectionDirection —
    /// instead of the single direction in SectionDirection.
    /// Use {Direction} in NameTemplate to differentiate them: "{Mark} {Direction}".
    /// </summary>
    public bool CreateAllDirections { get; set; } = false;

    /// <summary>
    /// When true, the section frame is aligned to the element's local orientation
    /// (wall/curve direction or FacingOrientation) instead of world axes.
    /// Falls back to world axes when no orientation can be detected.
    /// Only applies when Kind = Section.
    /// </summary>
    public bool AlignToElement { get; set; } = false;

    /// <summary>
    /// Optional view template name to apply after creation.
    /// Must match a template of the same ViewType in the project.
    /// </summary>
    public string? ViewTemplateName { get; set; }
}

public enum ViewKind
{
    Section,
    Plan,
    Isometric3D,
}

/// <summary>Which side the viewer stands on when the section/elevation is created.</summary>
public enum SectionDirection
{
    South,  // viewer on -Y side, looking in +Y
    North,  // viewer on +Y side, looking in -Y
    East,   // viewer on +X side, looking in -X
    West,   // viewer on -X side, looking in +X
}

public enum DuplicateHandling
{
    Skip,
    Overwrite,
    AppendSuffix,
}
