namespace SlabReinforcement.Config;

// ── Enums ────────────────────────────────────────────────────────────────────────

/// <summary>How a boundary segment's edge reinforcement is formed.</summary>
public enum EdgeTreatmentType
{
    None,
    /// <summary>Closing U-bar (П-образный) wrapping the edge top-to-bottom.</summary>
    UBar,
    /// <summary>The mat bars bend 90° down/up at the edge.</summary>
    Bend90,
    /// <summary>Straight bars run to the edge (cover only).</summary>
    Straight,
    /// <summary>Bars continue and anchor into the supporting beam/wall.</summary>
    IntoSupport,
}

public enum GroupShape { Straight, L, U, Hook90, Hook180, Custom }

public enum RegionKind
{
    /// <summary>Band of <see cref="BriefRegion.Width"/> centred on a named support, optionally
    /// limited to ±<see cref="BriefRegion.Extent"/> along the bars.</summary>
    SupportStrip,
    /// <summary>Axis-aligned rectangle <see cref="BriefRegion.BBox"/> = [x1,y1,x2,y2] (feet).</summary>
    BBox,
    /// <summary>Polygon <see cref="BriefRegion.Polygon"/> = [x,y,x,y,…] (feet).</summary>
    Polygon,
    /// <summary>A span of one boundary segment.</summary>
    EdgeRange,
    /// <summary>A single explicit line from <see cref="BriefRegion.From"/> to <see cref="BriefRegion.To"/>.</summary>
    Line,
}

public enum DirectionKind { Axis, World, AlongEdge, TowardSupport }

public enum DowelTarget { Wall, Stair, SlabAbove, Beam, Column, Other }

// ── Top-level brief ──────────────────────────────────────────────────────────────

/// <summary>
/// The structured JSON reinforcement brief produced by the agent and consumed by Generate Slab
/// Rebar. Richer than the flat CSV: per-boundary-segment edge treatment and arbitrary, fully
/// detailed additional rebar groups (incl. dowels into walls/stairs). One entry per slab.
/// Schema documented in slab-brief-schema.md.
/// </summary>
public sealed class SlabBrief
{
    public int SchemaVersion { get; set; } = 1;
    public UnitSystem Units { get; set; } = UnitSystem.Imperial;
    public List<BriefSlab> Slabs { get; set; } = [];
}

public sealed class BriefSlab
{
    public string? Mark { get; set; }                 // match the floor by Mark…
    public long ElementId { get; set; }               // …or by element id (0 = unused)
    public bool CleanExisting { get; set; } = true;

    public CoverConfig Cover { get; set; } = new();
    public BriefLengths Lengths { get; set; } = new();
    public BriefField Field { get; set; } = new();
    public List<BriefEdge> Edges { get; set; } = [];
    public BriefOpenings Openings { get; set; } = new();
    public List<BriefGroup> Groups { get; set; } = [];
}

// ── Lengths / lap ────────────────────────────────────────────────────────────────

public sealed class BriefLengths
{
    public Length MaxBarLength { get; set; } = new("40'-0\"");
    public BriefLap Lap { get; set; } = new();
}

public sealed class BriefLap
{
    public LapMode Mode { get; set; } = LapMode.Factor;     // Factor | Length | Aci
    public double Factor { get; set; } = 40;                // Mode=Factor: lap = factor · d_b
    public Length Length { get; set; } = new("2'-0\"");     // Mode=Length: fixed lap
    public bool Stagger { get; set; } = true;

    // Mode=Aci — ACI 318-19 Class B tension lap inputs (f'c / fy in psi).
    public double FcPsi { get; set; } = 4000;
    public double FyPsi { get; set; } = 60000;
    public bool Epoxy { get; set; }
    public bool Lightweight { get; set; }
    public bool AdequateSpacing { get; set; } = true;
}

// ── Field mat ────────────────────────────────────────────────────────────────────

public sealed class BriefField
{
    public FieldMode Mode { get; set; } = FieldMode.Sets;
    public BriefMat Bottom { get; set; } = new();
    public BriefTop Top { get; set; } = new();
}

public class BriefMat
{
    public LayerSpec X { get; set; } = new();
    public LayerSpec Y { get; set; } = new();
}

public sealed class BriefTop : BriefMat
{
    public TopMode Coverage { get; set; } = TopMode.None;
}

// ── Per-segment edges (#3) ───────────────────────────────────────────────────────

public sealed class BriefEdge
{
    /// <summary>Which boundary segments: "free" | "all" | space/comma list of indices, e.g. "0 2".</summary>
    public string Segments { get; set; } = "free";
    /// <summary>Informational: free | beam | wall | slab.</summary>
    public string Support { get; set; } = "free";
    public string? Adjacent { get; set; }             // mark of the supporting element
    public EdgeTreatment Treatment { get; set; } = new();
}

public sealed class EdgeTreatment
{
    public EdgeTreatmentType Type { get; set; } = EdgeTreatmentType.UBar;
    public string BarType { get; set; } = "#5";
    public Length Spacing { get; set; } = new(12);
    public Length Leg { get; set; } = new(12);
    public string Face { get; set; } = "both";        // both | top | bottom
    public string? AnchorInto { get; set; }           // beam | wall (for IntoSupport/Bend90)
    public Length AnchorLen { get; set; } = new("1'-0\"");
    public string Direction { get; set; } = "down";   // bend direction: down | up
}

// ── Opening trim ─────────────────────────────────────────────────────────────────

public sealed class BriefOpenings
{
    /// <summary>"auto" (size threshold) | "all" | "none" | index list.</summary>
    public string Trim { get; set; } = "auto";
    public string BarType { get; set; } = "#5";
    public int ExtraEachSide { get; set; } = 2;
    public bool UBars { get; set; } = true;
    public bool Diagonals { get; set; } = true;
}

// ── Arbitrary additional reinforcement groups (#4) ───────────────────────────────

public sealed class BriefGroup
{
    public string Name { get; set; } = "";
    public string Layer { get; set; } = "Top";        // Bottom | Top | Support | Dowel | …
    public string BarType { get; set; } = "#5";
    public Length? Spacing { get; set; }              // by spacing across the region…
    public int? Count { get; set; }                   // …or a fixed bar count

    public GroupShape Shape { get; set; } = GroupShape.Straight;
    public BriefDirection Direction { get; set; } = new();
    public BriefRegion Region { get; set; } = new();
    public string Face { get; set; } = "Top";         // Top | Bottom | Mid
    public Length? Length { get; set; }               // null = derived from region
    public BriefAnchorPair Anchor { get; set; } = new();
    public BriefDowel? Dowel { get; set; }            // out-of-plane starters (wall/stair/…)

    /// <summary>Bar ends that land at the slab edge get a 90° leg of this length bent into the
    /// slab (down for Top, up for Bottom) — the plan's "отгиб у торца" hook. Null = no bend.</summary>
    public Length? EdgeBend { get; set; }
}

public sealed class BriefDirection
{
    public DirectionKind Kind { get; set; } = DirectionKind.Axis;
    public string Axis { get; set; } = "X";           // X | Y
    public double Deg { get; set; }                   // Kind == World
    public int Edge { get; set; }                     // Kind == AlongEdge: boundary segment index
    public string? Support { get; set; }              // Kind == TowardSupport
}

public sealed class BriefRegion
{
    public RegionKind Kind { get; set; } = RegionKind.BBox;
    public double[]? BBox { get; set; }               // [x1,y1,x2,y2]
    public double[]? Polygon { get; set; }            // [x,y,x,y,…]
    public string? Support { get; set; }              // SupportStrip
    public Length? Width { get; set; }                // SupportStrip strip width
    public Length? Extent { get; set; }               // SupportStrip ± along bars
    public int Segment { get; set; }                  // EdgeRange: boundary segment index
    public Length? From { get; set; }                 // EdgeRange: distance along the segment
    public Length? To { get; set; }
    public double[]? LineFrom { get; set; }           // Line: [x,y]
    public double[]? LineTo { get; set; }             // Line: [x,y]
}

public sealed class BriefAnchorPair
{
    public BriefAnchor Start { get; set; } = new();
    public BriefAnchor End { get; set; } = new();
}

public sealed class BriefAnchor
{
    public string Type { get; set; } = "Straight";    // Straight | Hook90 | Hook180 | Bend
    public Length Len { get; set; } = new("1'-0\"");
}

public sealed class BriefDowel
{
    public DowelTarget Into { get; set; } = DowelTarget.Wall;
    public string? Target { get; set; }               // mark of the wall/stair/slab
    public Length EmbedLen { get; set; } = new("1'-6\"");   // into this slab
    public Length ProjectLen { get; set; } = new("2'-0\""); // out of this slab
    public string Bend { get; set; } = "90";          // 90 | none
    public string Direction { get; set; } = "up";     // up | down
    public double AngleDeg { get; set; }              // in-plane angle for the projecting leg
}
