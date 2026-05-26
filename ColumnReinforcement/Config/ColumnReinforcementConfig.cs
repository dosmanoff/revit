using System.Text.Json.Serialization;

namespace ColumnReinforcement.Config;

/// <summary>
/// Top-level configuration for a single column-reinforcement run.
/// Imperial defaults (inches) per repo convention; Metric is opt-in via <see cref="Units"/>.
/// Phase 1 covers rectangular/square columns with longitudinal bars + a single outer tie.
/// </summary>
public class ColumnReinforcementConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "unnamed";

    /// <summary>
    /// How plain numeric length values in this config are interpreted.
    /// <see cref="UnitSystem.Imperial"/> (default): inches; <see cref="UnitSystem.Metric"/>: mm.
    /// String values like "1'-3\"" are always parsed as feet-inches regardless.
    /// </summary>
    [JsonPropertyName("units")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UnitSystem Units { get; set; } = UnitSystem.Imperial;

    [JsonPropertyName("cover")]
    public CoverConfig Cover { get; set; } = new();

    [JsonPropertyName("longitudinal")]
    public LongitudinalConfig Longitudinal { get; set; } = new();

    [JsonPropertyName("stirrups")]
    public StirrupsConfig Stirrups { get; set; } = new();

    /// <summary>
    /// When true, before placing new rebar the engine deletes any prior bars
    /// in the same host column that carry the <c>ColumnReinforcement:</c> marker.
    /// Wired up in PR-06.
    /// </summary>
    [JsonPropertyName("cleanExisting")]
    public bool CleanExisting { get; set; } = true;

    /// <summary>Convert a config length to Revit internal feet using this config's <see cref="Units"/>.</summary>
    public double Ft(Length len) => len.ToFeet(Units);
}

/// <summary>Clear cover from the concrete face to the outer rebar.</summary>
public class CoverConfig
{
    /// <summary>Cover on the four vertical faces (to outer face of tie). ACI 318 §20.5.1.3 default ~1.5".</summary>
    [JsonPropertyName("sides")] public Length Sides { get; set; } = new(1.5);

    /// <summary>Cover at the top and bottom of the column (to ends of longitudinal bars).</summary>
    [JsonPropertyName("ends")]  public Length Ends  { get; set; } = new(1.5);
}

/// <summary>Longitudinal (vertical) reinforcement layout.</summary>
public class LongitudinalConfig
{
    /// <summary>RebarBarType .Name in the active document, e.g. "#8". Strict lookup — no auto-create.</summary>
    [JsonPropertyName("barType")] public string BarType { get; set; } = "#8";

    /// <summary>If true, only the four corner bars are placed (ignores AlongWidth/AlongDepth counts).</summary>
    [JsonPropertyName("cornerOnly")] public bool CornerOnly { get; set; }

    /// <summary>Number of bars along the column width (the "X" face), including the two corner bars. Min 2.</summary>
    [JsonPropertyName("barsAlongWidth")] public int BarsAlongWidth { get; set; } = 3;

    /// <summary>Number of bars along the column depth (the "Y" face), including the two corner bars. Min 2.</summary>
    [JsonPropertyName("barsAlongDepth")] public int BarsAlongDepth { get; set; } = 3;

    /// <summary>Optional RebarHookType .Name for the top end of longitudinal bars. <c>null</c> = no hook.</summary>
    [JsonPropertyName("hookTopType")]    public string? HookTopType    { get; set; }

    /// <summary>Optional RebarHookType .Name for the bottom end of longitudinal bars. <c>null</c> = no hook.</summary>
    [JsonPropertyName("hookBottomType")] public string? HookBottomType { get; set; }
}

/// <summary>Transverse (tie) reinforcement.</summary>
public class StirrupsConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    /// <summary>RebarBarType .Name, e.g. "#4". Strict lookup — no auto-create.</summary>
    [JsonPropertyName("barType")] public string BarType { get; set; } = "#4";

    /// <summary>Center-to-center spacing of ties along the column height.</summary>
    [JsonPropertyName("spacing")] public Length Spacing { get; set; } = new(8);

    /// <summary>
    /// RebarHookType .Name applied to both ends of the tie. Non-seismic default is a 90° standard
    /// hook per ACI 318 §25.3.2. Set to a 135° type for seismic detailing.
    /// </summary>
    [JsonPropertyName("hookType")] public string? HookType { get; set; } = "T1 - 90 deg";

    /// <summary>If true (Phase 2+), the tie is rotated 45° about the column axis (ACI 318 §25.7.2.3 allowed cases).</summary>
    [JsonPropertyName("rotate45")] public bool Rotate45 { get; set; }
}
