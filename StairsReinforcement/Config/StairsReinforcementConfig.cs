namespace StairsReinforcement.Config;

// ─────────────────────────────────────────────────────────────────────────────
//  Enums
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>How a bar set's quantity is expressed.</summary>
public enum SpacingMode
{
    /// <summary>Centre-to-centre spacing; count is derived from the run.</summary>
    Spacing,
    /// <summary>Explicit number of bars, evenly distributed over the run.</summary>
    Count,
}

/// <summary>How a bar end is anchored into / terminated at a support or free edge.</summary>
public enum AnchorMode
{
    /// <summary>Straight bar, extended by the set's anchor length.</summary>
    Straight,
    /// <summary>90° standard hook.</summary>
    Hook90,
    /// <summary>180° standard hook.</summary>
    Hook180,
    /// <summary>Run the straight development length into the supporting element.</summary>
    IntoSupport,
    /// <summary>Bend the bar up (toward the top face) at the end.</summary>
    BendUp,
    /// <summary>Bend the bar down (toward the soffit) at the end.</summary>
    BendDown,
}

/// <summary>Where the top (negative-moment) reinforcement is placed along a flight/landing.</summary>
public enum TopMode
{
    /// <summary>No top layer.</summary>
    None,
    /// <summary>Continuous top layer over the full span.</summary>
    Continuous,
    /// <summary>Only over the supports (both ends), length = SupportExtent.</summary>
    OverSupports,
    /// <summary>Only at the upper end (re-entrant corner / cantilever tip), length = SupportExtent.</summary>
    EndsOnly,
}

/// <summary>How a landing mat is modelled.</summary>
public enum FieldMode
{
    /// <summary>Individual bars, split at <see cref="LengthsConfig.MaxBarLength"/> and lapped.</summary>
    Bars,
    /// <summary>Native Revit <c>AreaReinforcement</c> system (no per-bar length control).</summary>
    AreaSystem,
}

/// <summary>How (if at all) the steps themselves are reinforced.</summary>
public enum StepMode
{
    None,
    /// <summary>One L-shaped bar following the tread + riser of each step.</summary>
    PerStepLBar,
    /// <summary>A single longitudinal bar along each step nosing.</summary>
    NosingBar,
}

/// <summary>The detail used to carry reinforcement around the flight↔landing fold (knee).</summary>
public enum KneeMode
{
    /// <summary>One bar bent to follow the fold, with a leg into each component.</summary>
    ContinuousBent,
    /// <summary>Two lapped hairpin/U bars — the safe detail for a re-entrant (top) corner.</summary>
    LappedHairpin,
    /// <summary>Crossed diagonal bars at the re-entrant corner (top tension does not bend round it).</summary>
    CrossedAtReentrant,
}

/// <summary>Which adjacent element a starter/dowel bar anchors into.</summary>
public enum StarterHost
{
    /// <summary>Pick the best-matching support found in the geometry context.</summary>
    Auto,
    SlabBelow,
    Beam,
    Wall,
    Foundation,
    None,
}

/// <summary>Shape of a starter/dowel bar.</summary>
public enum StarterForm
{
    /// <summary>Straight dowel: embedment + projection.</summary>
    Straight,
    /// <summary>L-bar: embedment leg + 90° + projection leg.</summary>
    L,
}

public enum LapMode { Factor, Length }

// ─────────────────────────────────────────────────────────────────────────────
//  Reusable per-set spec
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Full settings for ONE set of parallel bars (e.g. flight bottom main, landing top-X).
/// Every set carries its own bar type, quantity, side cover override and per-end anchorage,
/// so each set is independently and "redundantly" configurable per the project brief.
/// </summary>
public class BarSetSpec
{
    /// <summary>Place this set at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Exact <c>RebarBarType</c> name (strict lookup — must exist in the model).</summary>
    public string BarType { get; set; } = "#5";

    public SpacingMode SpacingMode { get; set; } = SpacingMode.Spacing;

    /// <summary>Centre-to-centre spacing (used when <see cref="SpacingMode"/> = Spacing).</summary>
    public Length Spacing { get; set; } = new(8);

    /// <summary>Explicit bar count (used when <see cref="SpacingMode"/> = Count).</summary>
    public int Count { get; set; } = 0;

    /// <summary>Optional override of the face cover for this set; null = use <see cref="CoverConfig"/>.</summary>
    public Length? Cover { get; set; }

    public AnchorMode StartAnchor { get; set; } = AnchorMode.Straight;
    public AnchorMode EndAnchor { get; set; } = AnchorMode.Straight;

    /// <summary>Straight extension / development length added at the start end.</summary>
    public Length StartAnchorLen { get; set; } = new(0);
    public Length EndAnchorLen { get; set; } = new(0);

    /// <summary>Hook type name for a hooked end (null = use a default standard hook).</summary>
    public string? StartHook { get; set; }
    public string? EndHook { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Root config
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Stair reinforcement settings. Loaded from a JSON file (camelCase keys, enums by name; see
/// <see cref="ConfigLoader"/>) or built per-stair from the assignments CSV. Lengths are
/// <see cref="Length"/> (plain number per <see cref="Units"/>, or a feet-inches string).
/// </summary>
public class StairsReinforcementConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "default";
    public UnitSystem Units { get; set; } = UnitSystem.Imperial;
    public bool CleanExisting { get; set; } = true;

    public CoverConfig Cover { get; set; } = new();
    public FlightConfig Flight { get; set; } = new();
    public LandingConfig Landing { get; set; } = new();
    public ConnectionsConfig Connections { get; set; } = new();
    public LengthsConfig Lengths { get; set; } = new();

    /// <summary>Convert a config length to Revit internal feet using this config's units.</summary>
    public double Ft(Length len) => len.ToFeet(Units);

    /// <summary>Convert an optional config length (set cover override) or fall back to a default.</summary>
    public double FtOr(Length? len, Length fallback) => (len ?? fallback).ToFeet(Units);
}

public class CoverConfig
{
    public Length Top { get; set; } = new(1.5);
    public Length Bottom { get; set; } = new(1.5);
    public Length Side { get; set; } = new(1.5);
}

/// <summary>Reinforcement of the inclined waist slab of each flight.</summary>
public class FlightConfig
{
    /// <summary>Bottom (soffit-side) longitudinal main bars, running up the slope.</summary>
    public BarSetSpec BottomMain { get; set; } = new() { BarType = "#5", Spacing = new(8) };

    /// <summary>Bottom transverse distribution bars, running across the width.</summary>
    public BarSetSpec BottomDist { get; set; } = new() { BarType = "#4", Spacing = new(12) };

    public TopMode TopMode { get; set; } = TopMode.OverSupports;

    /// <summary>Top (tread-side) longitudinal main bars.</summary>
    public BarSetSpec TopMain { get; set; } = new() { BarType = "#5", Spacing = new(8) };

    /// <summary>Top transverse distribution bars.</summary>
    public BarSetSpec TopDist { get; set; } = new() { BarType = "#4", Spacing = new(12) };

    /// <summary>For OverSupports/EndsOnly: distance the top bars run from each support face.</summary>
    public Length TopSupportExtent { get; set; } = new("3'-0\"");

    public StepConfig Steps { get; set; } = new();
}

/// <summary>Optional per-step reinforcement.</summary>
public class StepConfig
{
    public StepMode Mode { get; set; } = StepMode.None;
    public string BarType { get; set; } = "#3";
    /// <summary>Leg length for an L-bar / embedment for a nosing bar.</summary>
    public Length Leg { get; set; } = new(6);
    public Length Cover { get; set; } = new(1);
}

/// <summary>Reinforcement of a horizontal landing slab (a two-way mat, like a small slab).</summary>
public class LandingConfig
{
    public FieldMode Mode { get; set; } = FieldMode.Bars;

    public BarSetSpec BottomX { get; set; } = new() { BarType = "#5", Spacing = new(8) };
    public BarSetSpec BottomY { get; set; } = new() { BarType = "#5", Spacing = new(8) };

    public TopMode TopMode { get; set; } = TopMode.OverSupports;
    public BarSetSpec TopX { get; set; } = new() { BarType = "#5", Spacing = new(8) };
    public BarSetSpec TopY { get; set; } = new() { BarType = "#5", Spacing = new(8) };

    /// <summary>For OverSupports/EndsOnly: distance the top mat runs from each support face.</summary>
    public Length TopSupportExtent { get; set; } = new("3'-0\"");
}

/// <summary>Bars that tie the components together: the knee at folds, and starters into supports.</summary>
public class ConnectionsConfig
{
    public KneeConfig Knee { get; set; } = new();
    public StarterConfig Starters { get; set; } = new();

    /// <summary>Green/red connection dowels at every flight↔support junction (slab/landing/foundation).</summary>
    public DowelConfig Dowels { get; set; } = new();

    /// <summary>U-bars (shape 17) wrapping the supported edges of each landing into the walls.</summary>
    public PashkaConfig Pashki { get; set; } = new();
}

/// <summary>
/// Per junction, two bent bars lap the flight to the support meshes: GREEN runs ¼-span (min 4'-0")
/// into the flight from the support TOP mesh / top-plane; RED runs a short leg from the support
/// BOTTOM (or top) mesh into the flight bottom/mid plane. Each lies on / under its mesh (±1 mesh-dia).
/// At a foundation the support leg turns DOWN into the slab by <see cref="FoundationEmbed"/>.
/// </summary>
public class DowelConfig
{
    public bool Enabled { get; set; } = true;
    public string BarType { get; set; } = "#4";
    public SpacingMode SpacingMode { get; set; } = SpacingMode.Spacing;
    public Length Spacing { get; set; } = new(10);
    public int Count { get; set; } = 0;
    /// <summary>Green flight leg = max(¼ span, this).</summary>
    public Length GreenFlightMin { get; set; } = new("4'-0\"");
    /// <summary>Red flight leg, and the support leg of both into a slab/landing.</summary>
    public Length ShortLeg { get; set; } = new("1'-6\"");
    /// <summary>Vertical leg turned down into a foundation slab.</summary>
    public Length FoundationEmbed { get; set; } = new("0'-10\"");
}

/// <summary>U-bar (shape 17) sets along the wall-supported sides of a landing.</summary>
public class PashkaConfig
{
    public bool Enabled { get; set; } = true;
    public string BarType { get; set; } = "#4";
    public SpacingMode SpacingMode { get; set; } = SpacingMode.Spacing;
    public Length Spacing { get; set; } = new(10);
    public int Count { get; set; } = 0;
    /// <summary>Length of each horizontal leg reaching into the landing (laps the mesh).</summary>
    public Length Leg { get; set; } = new("1'-0\"");
}

/// <summary>Reinforcement carried around a flight↔landing (or flight↔flight) fold.</summary>
public class KneeConfig
{
    public bool Enabled { get; set; } = true;
    public KneeMode Mode { get; set; } = KneeMode.LappedHairpin;
    public string BarType { get; set; } = "#5";
    public SpacingMode SpacingMode { get; set; } = SpacingMode.Spacing;
    public Length Spacing { get; set; } = new(8);
    public int Count { get; set; } = 0;
    /// <summary>Length of each leg projecting into the adjoining component from the fold line.</summary>
    public Length Leg { get; set; } = new("2'-0\"");
}

/// <summary>Starter / dowel bars projecting from a supporting element into the stair.</summary>
public class StarterConfig
{
    public bool Enabled { get; set; } = true;
    public StarterHost Host { get; set; } = StarterHost.Auto;
    public StarterForm Form { get; set; } = StarterForm.L;
    public string BarType { get; set; } = "#5";
    public SpacingMode SpacingMode { get; set; } = SpacingMode.Spacing;
    public Length Spacing { get; set; } = new(8);
    public int Count { get; set; } = 0;
    /// <summary>Embedment length into the supporting element.</summary>
    public Length Embed { get; set; } = new("1'-6\"");
    /// <summary>Projection length into the stair (lap with the main bars).</summary>
    public Length Projection { get; set; } = new("2'-0\"");
}

public class LengthsConfig
{
    public Length MaxBarLength { get; set; } = new("40'-0\"");
    public LapMode LapMode { get; set; } = LapMode.Factor;
    public Length LapLength { get; set; } = new("2'-0\"");
    public double LapFactor { get; set; } = 40;
    public bool LapStagger { get; set; } = true;
}
