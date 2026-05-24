using System.Text.Json.Serialization;

namespace WallReinforcement.Config;

public class ReinforcementConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "unnamed";

    [JsonPropertyName("cover")]
    public CoverConfig Cover { get; set; } = new();

    [JsonPropertyName("faceMesh")]
    public FaceMeshConfig FaceMesh { get; set; } = new();

    [JsonPropertyName("openings")]
    public OpeningsConfig Openings { get; set; } = new();

    [JsonPropertyName("edges")]
    public EdgesConfig Edges { get; set; } = new();
}

public class CoverConfig
{
    [JsonPropertyName("exterior")] public double ExteriorMm { get; set; } = 30;
    [JsonPropertyName("interior")] public double InteriorMm { get; set; } = 25;
    [JsonPropertyName("top")]      public double TopMm      { get; set; } = 30;
    [JsonPropertyName("bottom")]   public double BottomMm   { get; set; } = 30;
    [JsonPropertyName("ends")]     public double EndsMm     { get; set; } = 30;
}

public class FaceMeshConfig
{
    [JsonPropertyName("exterior")] public FaceConfig? Exterior { get; set; } = new();
    [JsonPropertyName("interior")] public FaceConfig? Interior { get; set; } = new();
}

public class FaceConfig
{
    [JsonPropertyName("vertical")]   public LayerConfig Vertical   { get; set; } = new() { BarType = "Ø12", SpacingMm = 200 };
    [JsonPropertyName("horizontal")] public LayerConfig Horizontal { get; set; } = new() { BarType = "Ø10", SpacingMm = 200 };
}

public class LayerConfig
{
    [JsonPropertyName("barType")]   public string BarType   { get; set; } = "Ø10";
    [JsonPropertyName("spacing")]   public double SpacingMm { get; set; } = 200;
    [JsonPropertyName("hookType")]  public string? HookType { get; set; }
}

public class OpeningsConfig
{
    [JsonPropertyName("enabled")]      public bool   Enabled        { get; set; } = false;
    [JsonPropertyName("barType")]      public string BarType        { get; set; } = "Ø12";
    [JsonPropertyName("extensionMm")]  public double ExtensionMm    { get; set; } = 500;
    [JsonPropertyName("minWidthMm")]   public double MinWidthMm     { get; set; } = 300;
    [JsonPropertyName("diagonals")]    public DiagonalsConfig Diagonals { get; set; } = new();
}

public class DiagonalsConfig
{
    [JsonPropertyName("enabled")]  public bool   Enabled   { get; set; } = false;
    [JsonPropertyName("barType")]  public string BarType   { get; set; } = "Ø12";
    [JsonPropertyName("lengthMm")] public double LengthMm  { get; set; } = 700;
    [JsonPropertyName("angleDeg")] public double AngleDeg  { get; set; } = 45;
}

public class EdgesConfig
{
    [JsonPropertyName("top")]    public EdgeConfig Top    { get; set; } = new();
    [JsonPropertyName("bottom")] public EdgeConfig Bottom { get; set; } = new();
    [JsonPropertyName("ends")]   public EdgeConfig Ends   { get; set; } = new();
}

public class EdgeConfig
{
    [JsonPropertyName("enabled")]     public bool   Enabled     { get; set; } = false;
    [JsonPropertyName("barType")]     public string BarType     { get; set; } = "Ø10";
    [JsonPropertyName("legLengthMm")] public double LegLengthMm { get; set; } = 250;
    [JsonPropertyName("spacingMm")]   public double SpacingMm   { get; set; } = 200;
}
