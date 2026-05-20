using System.Text.Json.Serialization;

namespace SmartViews.Config;

public sealed class ViewConfig
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Incremented whenever the JSON schema changes.
    /// Version 0 (legacy): had a flat "cropOffset" field.
    /// Version 1 (current): has "offsets" with six independent sides.
    /// </summary>
    public int SchemaVersion { get; set; } = 0;

    public string ConfigFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Six-sided crop offsets (ft). Replaces the legacy uniform "cropOffset".
    /// Null only during deserialisation of v0 JSON — migration converts it.
    /// </summary>
    public CropOffsets? Offsets { get; set; }

    /// <summary>
    /// Legacy field present in v0 JSON. Read on deserialisation; never written.
    /// ConfigLoader.Migrate() converts this to Offsets.
    /// </summary>
    [JsonPropertyName("cropOffset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double LegacyCropOffset { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.Skip;

    /// <summary>
    /// View-range offsets applied to every Plan view. Null = Revit default.
    /// </summary>
    public PlanViewRangeConfig? PlanViewRange { get; set; }

    public List<ViewKindConfig> ViewKinds { get; set; } = [];

    public static ViewConfig Default() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Offsets       = new CropOffsets(),
        DuplicateHandling = DuplicateHandling.Skip,
        ViewKinds =
        [
            new ViewKindConfig
            {
                Kind = ViewKind.Section,
                NameTemplate = "{Mark} - {Direction}",
            },
        ],
    };
}

// -----------------------------------------------------------------------
// Crop offsets
// -----------------------------------------------------------------------

/// <summary>
/// Independent offsets (ft) for the six sides of the view crop box.
/// For sections:  Left/Right = horizontal, Top/Bottom = vertical,
///                Near = gap in front of the near face (elevation cutoff),
///                Far  = depth past the far face.
/// For plans:     Left/Right/Top/Bottom only (Near/Far are unused).
/// For isometric: Left/Right = X, Top/Bottom = Z, Far = Y (symmetric).
/// </summary>
public sealed class CropOffsets
{
    public double Left   { get; set; } = 1.0;
    public double Right  { get; set; } = 1.0;
    public double Top    { get; set; } = 1.0;
    public double Bottom { get; set; } = 1.0;
    public double Near   { get; set; } = 0.1;
    public double Far    { get; set; } = 1.0;

    public static CropOffsets Uniform(double v) =>
        new() { Left = v, Right = v, Top = v, Bottom = v, Near = 0.1, Far = v };
}

// -----------------------------------------------------------------------
// Plan view range
// -----------------------------------------------------------------------

public sealed class PlanViewRangeConfig
{
    public double TopOffset    { get; set; } = 7.5;
    public double CutOffset    { get; set; } = 4.0;
    public double BottomOffset { get; set; } = 0.0;
    public double ViewDepth    { get; set; } = 0.0;
}

// -----------------------------------------------------------------------
// Sheet target
// -----------------------------------------------------------------------

/// <summary>
/// Describes where to place the created view on a sheet.
/// All fields are optional; omitting SheetNumber skips placement entirely.
/// </summary>
public sealed class SheetTarget
{
    /// <summary>Sheet number as it appears in the title block (e.g. "A1.01").</summary>
    public string? SheetNumber { get; set; }

    /// <summary>Viewport type name. Null = first available type.</summary>
    public string? ViewportTypeName { get; set; }

    /// <summary>
    /// Centre of the viewport on the sheet in feet from the sheet origin.
    /// Null = Revit auto-positions (sheet centre).
    /// </summary>
    public PointConfig? ViewportCenter { get; set; }
}

public sealed class PointConfig
{
    public double X { get; set; }
    public double Y { get; set; }
}

// -----------------------------------------------------------------------
// ViewKindConfig
// -----------------------------------------------------------------------

public sealed class ViewKindConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ViewKind Kind { get; set; } = ViewKind.Section;

    /// <summary>
    /// Tokens: {Mark} {Level} {Type} {Index} {Direction} {Sheet}.
    /// Example: "{Mark} - {Direction} Elevation"
    /// </summary>
    public string NameTemplate { get; set; } = "{Mark} - {Type}";

    public string? ViewFamilyTypeName { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SectionDirection SectionDirection { get; set; } = SectionDirection.South;

    /// <summary>When true (Kind=Section), creates one view per SectionDirection.</summary>
    public bool CreateAllDirections { get; set; } = false;

    /// <summary>When true (Kind=Section), aligns the section frame to the element's axes.</summary>
    public bool AlignToElement { get; set; } = false;

    public string? ViewTemplateName { get; set; }

    /// <summary>Optional sheet placement after view creation.</summary>
    public SheetTarget? SheetTarget { get; set; }

    // ---- UI binding helper ----

    /// <summary>
    /// Gets or sets SheetTarget.SheetNumber.
    /// [JsonIgnore] — not serialised; used only for DataGrid binding.
    /// </summary>
    [JsonIgnore]
    public string? SheetNumber
    {
        get => SheetTarget?.SheetNumber;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (SheetTarget is not null)
                    SheetTarget.SheetNumber = null;
            }
            else
            {
                SheetTarget ??= new SheetTarget();
                SheetTarget.SheetNumber = value;
            }
        }
    }
}

// -----------------------------------------------------------------------
// Enums
// -----------------------------------------------------------------------

public enum ViewKind { Section, Plan, Isometric3D }

public enum SectionDirection
{
    South,  // viewer on −Y, looks +Y
    North,  // viewer on +Y, looks −Y
    East,   // viewer on +X, looks −X
    West,   // viewer on −X, looks +X
}

public enum DuplicateHandling { Skip, Overwrite, AppendSuffix }
