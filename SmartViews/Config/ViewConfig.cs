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
                NameTemplate = "{Mark} - Section",
            },
        ],
    };
}

public sealed class ViewKindConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ViewKind Kind { get; set; } = ViewKind.Section;

    /// <summary>
    /// Supports tokens: {Mark}, {Level}, {Type}, {Index}.
    /// Example: "{Mark} - {Level} Section"
    /// </summary>
    public string NameTemplate { get; set; } = "{Mark} - {Type}";

    /// <summary>Optional: pin the view to this ViewFamilyType name. Null = first match.</summary>
    public string? ViewFamilyTypeName { get; set; }

    /// <summary>
    /// Direction the viewer stands when looking at the element.
    /// Only applies when Kind = Section.
    /// South = viewer on -Y side looking north; North = viewer on +Y side looking south;
    /// East = viewer on +X side looking west; West = viewer on -X side looking east.
    /// When AlignToElement = true the directions are relative to the element's own axes.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SectionDirection SectionDirection { get; set; } = SectionDirection.South;

    /// <summary>
    /// When true, the section frame is aligned to the element's local orientation
    /// (wall direction, FacingOrientation, etc.) rather than world axes.
    /// Falls back to world axes when the element has no detectable orientation.
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
