using System.Text.Json.Serialization;

namespace WallReinforcement.Config;

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

    /// <summary>Convert a config length to Revit internal feet using this config's <see cref="Units"/>.</summary>
    public double Ft(Length len) => len.ToFeet(Units);
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
