using System.Text.Json.Serialization;
using WallReinforcement.Geometry;

namespace WallReinforcement.Config;

/// <summary>How leg / lap / extension lengths that are anchorage-governed are sized.</summary>
public enum AnchorMode
{
    /// <summary>Use the explicit length typed into each section (legLength / lapLength / extension).</summary>
    Explicit,
    /// <summary>Derive from ACI 318-19: development length for legs &amp; opening trim extensions,
    /// Class B tension lap for corner / T-junction laps. The explicit value is the fallback when
    /// the bar name is not ASTM (e.g. a metric "Ø12").</summary>
    Aci,
}

public class ReinforcementConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "unnamed";

    /// <summary>
    /// How plain numeric length values in this config are interpreted.
    /// <see cref="UnitSystem.Metric"/>: mm; <see cref="UnitSystem.Imperial"/>: inches.
    /// String values like "1'-3\"" are always parsed as feet-inches regardless.
    /// </summary>
    [JsonPropertyName("units")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UnitSystem Units { get; set; } = UnitSystem.Metric;

    [JsonPropertyName("cover")]
    public CoverConfig Cover { get; set; } = new();

    [JsonPropertyName("faceMesh")]
    public FaceMeshConfig FaceMesh { get; set; } = new();

    [JsonPropertyName("openings")]
    public OpeningsConfig Openings { get; set; } = new();

    [JsonPropertyName("edges")]
    public EdgesConfig Edges { get; set; } = new();

    [JsonPropertyName("ties")]
    public TiesConfig Ties { get; set; } = new();

    [JsonPropertyName("corners")]
    public CornersConfig Corners { get; set; } = new();

    [JsonPropertyName("tJunctions")]
    public TJunctionsConfig TJunctions { get; set; } = new();

    [JsonPropertyName("anchorage")]
    public AnchorageConfig Anchorage { get; set; } = new();

    /// <summary>Convert a config length to Revit internal feet using this config's <see cref="Units"/>.</summary>
    public double Ft(Length len) => len.ToFeet(Units);

    /// <summary>
    /// Tension lap-splice length in feet for a bar named <paramref name="barSize"/>. In
    /// <see cref="AnchorMode.Aci"/> mode this is the ACI 318-19 §25.5.2.1 Class B splice; otherwise
    /// (or if the bar name is not ASTM) it returns <paramref name="explicitFt"/>.
    /// </summary>
    public double LapFeet(string barSize, double explicitFt)
    {
        if (Anchorage.Mode != AnchorMode.Aci) return explicitFt;
        try
        {
            double inches = AciAnchorageCalculator.TensionLapSpliceClassBIn(Anchorage.ToInputs(barSize, isTopBar: false));
            return UnitConv_InchesToFeet(inches);
        }
        catch { return explicitFt; }
    }

    /// <summary>
    /// Tension development (anchorage) length in feet for a bar named <paramref name="barSize"/> —
    /// used for U-bar legs and opening-trim extensions. In <see cref="AnchorMode.Aci"/> mode this is
    /// the ACI 318-19 §25.4.2.3 development length; otherwise it returns <paramref name="explicitFt"/>.
    /// </summary>
    public double DevLengthFeet(string barSize, double explicitFt, bool isTopBar = false)
    {
        if (Anchorage.Mode != AnchorMode.Aci) return explicitFt;
        try
        {
            double inches = AciAnchorageCalculator.DevelopmentLengthTensionIn(Anchorage.ToInputs(barSize, isTopBar));
            return UnitConv_InchesToFeet(inches);
        }
        catch { return explicitFt; }
    }

    private static double UnitConv_InchesToFeet(double inches) => inches / 12.0;
}

public class AnchorageConfig
{
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnchorMode Mode { get; set; } = AnchorMode.Explicit;

    [JsonPropertyName("fcPsi")]           public double FcPsi { get; set; } = 4000;
    [JsonPropertyName("fyPsi")]           public double FyPsi { get; set; } = 60000;
    [JsonPropertyName("epoxy")]           public bool   Epoxy { get; set; }
    [JsonPropertyName("lightweight")]     public bool   Lightweight { get; set; }
    [JsonPropertyName("adequateSpacing")] public bool   AdequateSpacing { get; set; } = true;

    public AciAnchorageCalculator.Inputs ToInputs(string barSize, bool isTopBar) => new()
    {
        BarSize = barSize,
        FcPsi = FcPsi,
        FyPsi = FyPsi,
        IsTopBar = isTopBar,
        IsEpoxyCoated = Epoxy,
        IsLightweight = Lightweight,
        AdequateSpacing = AdequateSpacing,
    };
}

public class CoverConfig
{
    [JsonPropertyName("exterior")] public Length Exterior { get; set; } = new(30);
    [JsonPropertyName("interior")] public Length Interior { get; set; } = new(25);
    [JsonPropertyName("top")]      public Length Top      { get; set; } = new(30);
    [JsonPropertyName("bottom")]   public Length Bottom   { get; set; } = new(30);
    [JsonPropertyName("ends")]     public Length Ends     { get; set; } = new(30);
}

public class FaceMeshConfig
{
    [JsonPropertyName("exterior")] public FaceConfig? Exterior { get; set; } = new();
    [JsonPropertyName("interior")] public FaceConfig? Interior { get; set; } = new();
}

public class FaceConfig
{
    [JsonPropertyName("vertical")]   public LayerConfig Vertical   { get; set; } = new() { BarType = "Ø12", Spacing = new Length(200) };
    [JsonPropertyName("horizontal")] public LayerConfig Horizontal { get; set; } = new() { BarType = "Ø10", Spacing = new Length(200) };
}

public class LayerConfig
{
    [JsonPropertyName("barType")]  public string BarType  { get; set; } = "Ø10";
    [JsonPropertyName("spacing")]  public Length Spacing  { get; set; } = new(200);
    [JsonPropertyName("hookType")] public string? HookType { get; set; }
}

public class OpeningsConfig
{
    [JsonPropertyName("enabled")]   public bool   Enabled   { get; set; } = false;
    [JsonPropertyName("barType")]   public string BarType   { get; set; } = "Ø12";
    [JsonPropertyName("extension")] public Length Extension { get; set; } = new(500);
    [JsonPropertyName("minWidth")]  public Length MinWidth  { get; set; } = new(300);
    [JsonPropertyName("diagonals")] public DiagonalsConfig Diagonals { get; set; } = new();
}

public class DiagonalsConfig
{
    [JsonPropertyName("enabled")]  public bool   Enabled  { get; set; } = false;
    [JsonPropertyName("barType")]  public string BarType  { get; set; } = "Ø12";
    [JsonPropertyName("length")]   public Length Length   { get; set; } = new(700);
    [JsonPropertyName("angleDeg")] public double AngleDeg { get; set; } = 45;
}

public class EdgesConfig
{
    [JsonPropertyName("top")]    public EdgeConfig Top    { get; set; } = new();
    [JsonPropertyName("bottom")] public EdgeConfig Bottom { get; set; } = new();
    [JsonPropertyName("ends")]   public EdgeConfig Ends   { get; set; } = new();
}

public class EdgeConfig
{
    [JsonPropertyName("enabled")]   public bool   Enabled   { get; set; } = false;
    [JsonPropertyName("barType")]   public string BarType   { get; set; } = "Ø10";
    [JsonPropertyName("legLength")] public Length LegLength { get; set; } = new(250);
    [JsonPropertyName("spacing")]   public Length Spacing   { get; set; } = new(200);
}

public class TiesConfig
{
    [JsonPropertyName("enabled")]      public bool   Enabled      { get; set; } = false;
    [JsonPropertyName("barType")]      public string BarType      { get; set; } = "Ø8";
    [JsonPropertyName("spacingX")]     public Length SpacingX     { get; set; } = new(400);
    [JsonPropertyName("spacingY")]     public Length SpacingY     { get; set; } = new(400);
    [JsonPropertyName("minThickness")] public Length MinThickness { get; set; } = new(250);
}

public class CornersConfig
{
    [JsonPropertyName("enabled")]   public bool   Enabled   { get; set; } = false;
    [JsonPropertyName("barType")]   public string BarType   { get; set; } = "Ø12";
    [JsonPropertyName("lapLength")] public Length LapLength { get; set; } = new(400);
    [JsonPropertyName("spacing")]   public Length Spacing   { get; set; } = new(200);
}

public class TJunctionsConfig
{
    [JsonPropertyName("enabled")]   public bool   Enabled   { get; set; } = false;
    [JsonPropertyName("barType")]   public string BarType   { get; set; } = "Ø12";
    [JsonPropertyName("lapLength")] public Length LapLength { get; set; } = new(400);
    [JsonPropertyName("spacing")]   public Length Spacing   { get; set; } = new(200);
}
