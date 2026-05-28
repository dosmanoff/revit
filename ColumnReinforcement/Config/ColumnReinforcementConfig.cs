using Autodesk.Revit.DB;
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
    /// Foundation dowels (starter bars) connecting the column cage to the slab
    /// below. Disabled by default. When enabled, the engine looks for a slab
    /// under the column and places one dowel per longitudinal-bar position.
    /// </summary>
    [JsonPropertyName("dowels")]
    public DowelsConfig Dowels { get; set; } = new();

    /// <summary>
    /// Upper splices (continuation bars) extending the column cage into the
    /// column / slab above. Disabled by default.
    /// </summary>
    [JsonPropertyName("upperSplices")]
    public UpperSplicesConfig UpperSplices { get; set; } = new();

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

/// <summary>
/// What the top end of a single longitudinal bar does. Assigned per bar via
/// <see cref="LongitudinalConfig.TopDefault"/> + <see cref="LongitudinalConfig.TopModes"/>.
/// </summary>
public enum BarTopMode
{
    /// <summary>Run straight to the column top (Phase-1 behaviour).</summary>
    Straight,

    /// <summary>
    /// Crank the bar itself: vertical inside this column, a diagonal offsetting
    /// to the upper column's (smaller / shifted) cage position, then a vertical
    /// penetration into the upper column. The bar is one continuous piece — no
    /// separate splice bar. Avoids clashing with the upper column's cage.
    /// </summary>
    Cranked,

    /// <summary>90° bend into the slab above; the bar terminates with a horizontal leg in the slab.</summary>
    BentToSlab,
}

/// <summary>Longitudinal (vertical) reinforcement layout.</summary>
public class LongitudinalConfig
{
    /// <summary>RebarBarType .Name in the active document, e.g. "#8". Strict lookup — no auto-create.</summary>
    [JsonPropertyName("barType")] public string BarType { get; set; } = "#8";

    /// <summary>If true, only the four corner bars are placed (ignores AlongWidth/AlongDepth counts).</summary>
    [JsonPropertyName("cornerOnly")] public bool CornerOnly { get; set; }

    /// <summary>Number of bars along the column width (the "X" face), including the two corner bars. Min 2. Rectangular columns only.</summary>
    [JsonPropertyName("barsAlongWidth")] public int BarsAlongWidth { get; set; } = 3;

    /// <summary>Number of bars along the column depth (the "Y" face), including the two corner bars. Min 2. Rectangular columns only.</summary>
    [JsonPropertyName("barsAlongDepth")] public int BarsAlongDepth { get; set; } = 3;

    /// <summary>Number of bars evenly spaced around the circumference. Round columns only. Min 3.</summary>
    [JsonPropertyName("barsAround")] public int BarsAround { get; set; } = 8;

    /// <summary>Optional RebarHookType .Name for the top end of longitudinal bars. <c>null</c> = no hook.</summary>
    [JsonPropertyName("hookTopType")]    public string? HookTopType    { get; set; }

    /// <summary>Optional RebarHookType .Name for the bottom end of longitudinal bars. <c>null</c> = no hook.</summary>
    [JsonPropertyName("hookBottomType")] public string? HookBottomType { get; set; }

    /// <summary>
    /// Default top mode applied to every bar that isn't overridden by
    /// <see cref="TopModes"/>. <see cref="BarTopMode.Straight"/> reproduces
    /// Phase-1 behaviour.
    /// </summary>
    [JsonPropertyName("topDefault")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BarTopMode TopDefault { get; set; } = BarTopMode.Straight;

    /// <summary>
    /// Per-bar top-mode overrides as a space- or semicolon-separated list of
    /// <c>selector:mode</c> tokens. Selector is a 0-based bar index (in the cage
    /// enumeration order — see the CSV guide), or a group keyword
    /// <c>corners</c> / <c>edges</c> / <c>all</c>. Mode is <c>Straight</c> /
    /// <c>Cranked</c> / <c>BentToSlab</c> (or the short forms <c>S</c> / <c>C</c> / <c>B</c>).
    /// Precedence: explicit index &gt; group keyword &gt; <see cref="TopDefault"/>.
    /// Example: <c>"corners:Cranked 8:BentToSlab"</c>. Null/empty = all bars use the default.
    /// </summary>
    [JsonPropertyName("topModes")] public string? TopModes { get; set; }

    /// <summary>
    /// Horizontal leg length inside the slab above for bars in
    /// <see cref="BarTopMode.BentToSlab"/> mode.
    /// </summary>
    [JsonPropertyName("topBentLeg")] public Length TopBentLeg { get; set; } = new(12);

    /// <summary>
    /// <see cref="BarTopMode.BentToSlab"/> direction: when <c>true</c> (default) the
    /// horizontal leg bends OUTWARD, along the bar's outward face normal, so legs from
    /// opposite faces fan apart into the surrounding slab. When <c>false</c> the leg
    /// bends inward toward the column centre (legacy behaviour) — fine for large columns
    /// but causes opposite-face legs to cross in small ones.
    /// </summary>
    [JsonPropertyName("topBentOutward")] public bool TopBentOutward { get; set; } = true;

    /// <summary>Cranked mode: how much the upper column's cage is inset on each side (the offset the bar cranks to).</summary>
    [JsonPropertyName("crankUpperInset")] public Length CrankUpperInset { get; set; } = new(2);

    /// <summary>Cranked mode: vertical-over-horizontal slope of the diagonal. ACI 318-19 §10.7.4.1 caps at 1:6 (so default 6).</summary>
    [JsonPropertyName("crankSlope")] public double CrankSlope { get; set; } = 6.0;

    /// <summary>Cranked mode: distance from the column top down to the first bend (top of the lower vertical leg).</summary>
    [JsonPropertyName("crankLowerBendOffset")] public Length CrankLowerBendOffset { get; set; } = new(6);

    /// <summary>Cranked mode: how far the bar penetrates up INTO the upper column past the second bend (lap with the upper cage).</summary>
    [JsonPropertyName("crankPenetration")] public Length CrankPenetration { get; set; } = new(24);
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
    /// hook per ACI 318 §25.3.2 — matches Revit's OOTB <c>"Stirrup/Tie - 90 deg."</c> hook type
    /// (note the trailing period). Set to <c>"Stirrup/Tie - 135 deg."</c> for seismic detailing,
    /// or any other RebarHookType .Name present in the active document.
    /// </summary>
    [JsonPropertyName("hookType")] public string? HookType { get; set; } = "Stirrup/Tie - 90 deg.";

    /// <summary>If true (Phase 2+), the tie is rotated 45° about the column axis (ACI 318 §25.7.2.3 allowed cases).</summary>
    [JsonPropertyName("rotate45")] public bool Rotate45 { get; set; }

    /// <summary>
    /// Distance from the bottom face of the column to the lowest tie. When
    /// <c>null</c>, defaults to <see cref="CoverConfig.Ends"/>. Useful for
    /// skipping the joint zone where the slab/beam reinforcement runs.
    /// </summary>
    [JsonPropertyName("offsetBottom")] public Length? OffsetBottom { get; set; }

    /// <summary>
    /// Distance from the top face of the column to the highest tie. When
    /// <c>null</c>, defaults to <see cref="CoverConfig.Ends"/>.
    /// </summary>
    [JsonPropertyName("offsetTop")] public Length? OffsetTop { get; set; }

    /// <summary>
    /// Densified-tie zones at the top and bottom of the column. Each zone overrides
    /// <see cref="Spacing"/> with its own tighter step within a given length. Disabled
    /// by default; when both are disabled the engine behaves exactly as in Phase 1.
    /// </summary>
    [JsonPropertyName("confinement")] public ConfinementZonesConfig Confinement { get; set; } = new();
}

/// <summary>Top and bottom confinement-zone settings.</summary>
public class ConfinementZonesConfig
{
    [JsonPropertyName("top")]    public ConfinementZoneConfig Top    { get; set; } = new();
    [JsonPropertyName("bottom")] public ConfinementZoneConfig Bottom { get; set; } = new();
}

/// <summary>
/// One confinement zone (either top or bottom). The zone runs from the matching face
/// inward by <see cref="ZoneLength"/> or by <see cref="ZoneFraction"/>·height; when both
/// are set, <see cref="ZoneLength"/> wins. Within the zone, ties are placed at
/// <see cref="Spacing"/> instead of the main spacing.
/// </summary>
public class ConfinementZoneConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    /// <summary>Densified tie spacing inside the zone, typically tighter than the main spacing.</summary>
    [JsonPropertyName("spacing")] public Length Spacing { get; set; } = new(4);

    /// <summary>Absolute zone length from the face. Wins over <see cref="ZoneFraction"/> when both are set.</summary>
    [JsonPropertyName("zoneLength")] public Length? ZoneLength { get; set; }

    /// <summary>Fraction of the column's clear height (0–1). Used only when <see cref="ZoneLength"/> is null.</summary>
    [JsonPropertyName("zoneFraction")] public double? ZoneFraction { get; set; }
}

/// <summary>Shape variants for foundation dowels.</summary>
public enum DowelForm
{
    /// <summary>Single vertical bar from inside the host element up into the column.</summary>
    Straight,

    /// <summary>90° bend at the bottom; horizontal leg extends inside the slab toward the column centre. Slab/foundation hosts only.</summary>
    L,
}

/// <summary>Kind of host element from which dowels are extracted.</summary>
public enum DowelHost
{
    /// <summary>Search foundation, then floors (legacy behaviour; respects <see cref="DowelsConfig.OnlyStructuralFoundation"/>).</summary>
    Auto,

    /// <summary><see cref="BuiltInCategory.OST_StructuralFoundation"/> only.</summary>
    Foundation,

    /// <summary><see cref="BuiltInCategory.OST_Floors"/> only.</summary>
    Floor,

    /// <summary>
    /// <see cref="BuiltInCategory.OST_StructuralColumns"/> below — used when the upper column has a
    /// section so much smaller than the lower that a Cranked splice would violate the ACI 1:6 slope.
    /// Dowels run from inside the lower column up into the current column; bar type matches the
    /// current cage; positions sit 1·d_bar offset along the face from each cage position so the
    /// dowel and the cage bar lap non-contact.
    /// </summary>
    Column,

    /// <summary>
    /// <see cref="BuiltInCategory.OST_StructuralFraming"/> below — beams. Rare; used when the
    /// column lands on a transfer beam.
    /// </summary>
    Beam,
}

/// <summary>
/// Foundation dowels (starter bars). Placed at the same (x, y) positions as the
/// longitudinal cage so the lap splice with the column bars is concentric.
/// </summary>
public class DowelsConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    /// <summary>RebarBarType .Name, e.g. "#8". Typically matches the longitudinal bar size.</summary>
    [JsonPropertyName("barType")] public string BarType { get; set; } = "#8";

    [JsonPropertyName("form")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DowelForm Form { get; set; } = DowelForm.L;

    /// <summary>Vertical leg above the slab top — the lap-splice length with the column longitudinal.</summary>
    [JsonPropertyName("extension")] public Length Extension { get; set; } = new(24);

    /// <summary>Vertical leg below the slab top — embedment into the slab.</summary>
    [JsonPropertyName("embedment")] public Length Embedment { get; set; } = new(6);

    /// <summary>Horizontal leg length for <see cref="DowelForm.L"/>. Ignored for Straight.</summary>
    [JsonPropertyName("legLength")] public Length LegLength { get; set; } = new(9);

    /// <summary>Optional RebarHookType .Name applied at the top end of the dowel.</summary>
    [JsonPropertyName("hookTopType")]    public string? HookTopType    { get; set; }

    /// <summary>Optional RebarHookType .Name applied at the bottom end of the dowel.</summary>
    [JsonPropertyName("hookBottomType")] public string? HookBottomType { get; set; }

    /// <summary>
    /// Only relevant when <see cref="Host"/> is <see cref="DowelHost.Auto"/>. When true, only
    /// <see cref="BuiltInCategory.OST_StructuralFoundation"/> elements are considered as
    /// potential hosts; when false, regular floors (<see cref="BuiltInCategory.OST_Floors"/>) also qualify.
    /// </summary>
    [JsonPropertyName("onlyStructuralFoundation")] public bool OnlyStructuralFoundation { get; set; } = true;

    /// <summary>
    /// Which structural element the dowels are extracted from. <see cref="DowelHost.Auto"/>
    /// (default) preserves the original behaviour driven by <see cref="OnlyStructuralFoundation"/>;
    /// the explicit values force a single host kind. <see cref="DowelHost.Column"/> is for
    /// the column-section-change case where Cranked splice doesn't fit.
    /// </summary>
    [JsonPropertyName("host")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DowelHost Host { get; set; } = DowelHost.Auto;

    /// <summary>
    /// Optional subset of cage positions to place dowels at, using the same selector
    /// vocabulary as <see cref="LongitudinalConfig.TopModes"/>: space/<c>;</c>/<c>,</c>-separated
    /// 0-based indices and/or the keywords <c>all</c> / <c>corners</c> / <c>edges</c> /
    /// <c>+x</c> / <c>-x</c> / <c>+y</c> / <c>-y</c>. A position is doweled if it matches any
    /// token. <c>null</c>/empty (the default) dowels EVERY position. Use this to place
    /// starters only where the column below has no continuing bar — e.g. when the lower
    /// column bent part of its cage into the slab, dowel just those faces.
    /// </summary>
    [JsonPropertyName("positions")] public string? Positions { get; set; }
}

/// <summary>Shape variants for upper splices.</summary>
public enum UpperSpliceForm
{
    /// <summary>Single vertical bar continuing the column longitudinal into the column / slab above.</summary>
    Straight,

    /// <summary>Vertical leg up to near the slab top, then 90° bend with a horizontal leg anchoring inside the slab above.</summary>
    Bent,

    /// <summary>
    /// Three-segment cranked bar for splicing into a smaller upper column: a
    /// vertical leg inside the lower column, a diagonal leg that offsets to the
    /// upper column's (smaller) cage position, and a vertical leg inside the
    /// upper column. Slope of the diagonal segment ≤ 1:6 per ACI 318-19 §10.7.4.1.
    /// </summary>
    Cranked,
}

/// <summary>
/// Upper splices — bars that lap with the column longitudinal near the top of the
/// column and extend above the column top. At intermediate floors a straight splice
/// continues into the column above; at a roof level a bent splice anchors into the
/// slab above.
/// </summary>
public class UpperSplicesConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    /// <summary>RebarBarType .Name. Typically matches the longitudinal bar size.</summary>
    [JsonPropertyName("barType")] public string BarType { get; set; } = "#8";

    [JsonPropertyName("form")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UpperSpliceForm Form { get; set; } = UpperSpliceForm.Straight;

    /// <summary>
    /// Length of the splice that sits INSIDE the column near the top, overlapping
    /// the column longitudinal — the lap-splice length per ACI 318 §25.5.
    /// </summary>
    [JsonPropertyName("lapInsideColumn")] public Length LapInsideColumn { get; set; } = new(24);

    /// <summary>
    /// Straight form only: vertical extension above the column top (or above the
    /// slab top, when a slab is above and <see cref="IgnoreSlabAbove"/> is false).
    /// </summary>
    [JsonPropertyName("extension")] public Length Extension { get; set; } = new(24);

    /// <summary>Bent form only: horizontal leg length anchored inside the slab above.</summary>
    [JsonPropertyName("bentLegLength")] public Length BentLegLength { get; set; } = new(12);

    /// <summary>
    /// Cranked form only: how much the upper column's cage is inset from the
    /// lower cage on each side. E.g. <c>2"</c> for an upper column 4" smaller in
    /// width and depth (assuming both columns centred). Each lower bar at
    /// <c>(x, y)</c> in the lower frame maps to <c>(x − sign(x)·inset, y − sign(y)·inset)</c>
    /// in the upper frame.
    /// </summary>
    [JsonPropertyName("upperCageInset")] public Length UpperCageInset { get; set; } = new(2);

    /// <summary>
    /// Cranked form only: vertical-over-horizontal slope ratio of the diagonal
    /// segment. ACI 318-19 §10.7.4.1: "slope shall not exceed 1 in 6 with respect
    /// to the axis of the member", so the default 6.0 is the steepest allowed.
    /// </summary>
    [JsonPropertyName("crankedSlopeRatio")] public double CrankedSlopeRatio { get; set; } = 6.0;

    /// <summary>
    /// Cranked form only: distance from the column top down to the first bend
    /// (the top of the lower vertical leg). Typical value 4–6 inches.
    /// </summary>
    [JsonPropertyName("lowerBendOffsetFromTop")] public Length LowerBendOffsetFromTop { get; set; } = new(6);

    /// <summary>
    /// Straight form only: when true, measure <see cref="Extension"/> from the column
    /// top regardless of whether a slab is above. Useful when the joint hasn't been
    /// modelled yet or the slab is on a separate workshare.
    /// </summary>
    [JsonPropertyName("ignoreSlabAbove")] public bool IgnoreSlabAbove { get; set; }

    /// <summary>Optional RebarHookType .Name applied at the top end.</summary>
    [JsonPropertyName("hookTopType")]    public string? HookTopType    { get; set; }

    /// <summary>Optional RebarHookType .Name applied at the bottom end (inside the column).</summary>
    [JsonPropertyName("hookBottomType")] public string? HookBottomType { get; set; }
}
