namespace SlabReinforcement.Config;

/// <summary>How the field (main) reinforcement is modelled.</summary>
public enum FieldMode
{
    /// <summary>Individual bars, split at <see cref="LengthsConfig.MaxBarLength"/> and lapped.</summary>
    Bars,
    /// <summary>Rebar sets — one Rebar element per run with a spacing layout, shown as the
    /// representative (middle) bar; laps by splitting the set lengthwise.</summary>
    Sets,
    /// <summary>Native Revit AreaReinforcement system (openings excluded via inner loops).</summary>
    AreaSystem,
}

public enum TopMode { None, Continuous, OverSupports, Edges }

public enum LapMode { Factor, Length }

public enum EdgeAnchorMode { Straight, Hook90, Hook180, IntoSupport }

/// <summary>
/// Slab reinforcement settings. Loaded from a JSON file (camelCase keys, see
/// <see cref="ConfigLoader"/>) or built per-slab from the assignments CSV. Lengths are
/// <see cref="Length"/> (plain number per <see cref="Units"/>, or feet-inches string).
/// </summary>
public class SlabReinforcementConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "default";
    public UnitSystem Units { get; set; } = UnitSystem.Imperial;
    public FieldMode FieldMode { get; set; } = FieldMode.Bars;

    public CoverConfig Cover { get; set; } = new();
    public FieldConfig Field { get; set; } = new();
    public LengthsConfig Lengths { get; set; } = new();
    public AnchorsConfig Anchors { get; set; } = new();
    public EdgesConfig Edges { get; set; } = new();
    public OpeningsConfig Openings { get; set; } = new();

    public bool CleanExisting { get; set; } = true;

    /// <summary>Convert a config length to Revit internal feet using this config's units.</summary>
    public double Ft(Length len) => len.ToFeet(Units);
}

public class CoverConfig
{
    public Length Top { get; set; } = new(1.5);
    public Length Bottom { get; set; } = new(1.5);
    public Length Side { get; set; } = new(1.5);
}

public class FieldConfig
{
    public LayerSpec BottomX { get; set; } = new();
    public LayerSpec BottomY { get; set; } = new();
    public TopMode TopMode { get; set; } = TopMode.None;
    public LayerSpec TopX { get; set; } = new();
    public LayerSpec TopY { get; set; } = new();
}

public class LayerSpec
{
    public string BarType { get; set; } = "#5";
    public Length Spacing { get; set; } = new(12);
}

public class LengthsConfig
{
    public Length MaxBarLength { get; set; } = new("40'-0\"");
    public LapMode LapMode { get; set; } = LapMode.Factor;
    public Length LapLength { get; set; } = new("2'-0\"");
    public double LapFactor { get; set; } = 40;
    public bool LapStagger { get; set; } = true;
}

public class AnchorsConfig
{
    public EdgeAnchorMode EdgeAnchorMode { get; set; } = EdgeAnchorMode.Straight;
    public Length EdgeAnchorLen { get; set; } = new("2'-0\"");
}

public class EdgesConfig
{
    public bool UBarsEnabled { get; set; }
    public string BarType { get; set; } = "#5";
    public Length Spacing { get; set; } = new(12);
    public Length Leg { get; set; } = new(12);
    public string Selector { get; set; } = "free";
}

public class OpeningsConfig
{
    public bool TrimEnabled { get; set; }
    public string BarType { get; set; } = "#5";
    public int ExtraEachSide { get; set; } = 2;
    public bool UBars { get; set; } = true;
    public bool Diagonals { get; set; } = true;
    public string DiagBarType { get; set; } = "#5";
    public string Selector { get; set; } = "auto";   // only openings the classifier flags (skips shafts / edge-adjacent)
}
