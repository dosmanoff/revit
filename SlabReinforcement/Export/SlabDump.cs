using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlabReinforcement.Export;

/// <summary>
/// Serializable description of one or more slabs for the external reinforcement agent.
/// Property names serialize to snake_case (see <see cref="JsonOptions"/>) to match the
/// ColumnReinforcement dump convention. Schema documented in slab-dump-schema.md (PR-05).
/// </summary>
public sealed class SlabDump
{
    public DocumentInfo Document { get; set; } = new();
    public string GeneratedAt { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string UnitsNote { get; set; } =
        "Section dimensions and cover are in inches; elevations and plan coords in feet " +
        "(Revit internal). Angles in degrees from world +X.";

    public List<LevelInfo> Levels { get; set; } = [];
    public Dictionary<string, FloorTypeInfo> FloorTypesInUse { get; set; } = [];
    public List<BarTypeInfo> AvailableRebarBarTypes { get; set; } = [];
    public List<HookTypeInfo> AvailableRebarHookTypes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<SlabInfo> Slabs { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public sealed class DocumentInfo
{
    public string Title { get; set; } = "";
    public string? Path { get; set; }
}

public sealed class LevelInfo
{
    public string Name { get; set; } = "";
    public double ElevationFt { get; set; }
    public long Id { get; set; }
}

public sealed class FloorTypeInfo
{
    public string Family { get; set; } = "";
    public string Type { get; set; } = "";
    public long Id { get; set; }
    public double ThicknessIn { get; set; }
    public string? StructuralMaterial { get; set; }
}

public sealed class BarTypeInfo
{
    public string Name { get; set; } = "";
    public double NominalDiameterIn { get; set; }
}

public sealed class HookTypeInfo
{
    public string Name { get; set; } = "";
}

public sealed class SlabInfo
{
    public long ElementId { get; set; }
    public string? Mark { get; set; }
    public string? Comments { get; set; }
    public string Family { get; set; } = "";
    public string Type { get; set; } = "";
    public long TypeId { get; set; }

    public LevelRef? Level { get; set; }
    public double ThicknessIn { get; set; }
    public double TopElevationFt { get; set; }
    public double BottomElevationFt { get; set; }
    public bool IsStructural { get; set; }
    public bool IsFoundation { get; set; }
    public double AreaSf { get; set; }

    public CoverInfo RebarCover { get; set; } = new();
    public LocalBasisInfo LocalBasis { get; set; } = new();
    public BboxInfo Bbox { get; set; } = new();

    public List<BoundaryEdgeInfo> Boundary { get; set; } = [];
    public List<OpeningInfo> Openings { get; set; } = [];
    public ContextInfo Context { get; set; } = new();
    public HintsInfo Hints { get; set; } = new();
}

public sealed class LevelRef
{
    public string Name { get; set; } = "";
    public long Id { get; set; }
    public double ElevationFt { get; set; }
}

public sealed class CoverInfo
{
    public CoverFace? Top { get; set; }
    public CoverFace? Bottom { get; set; }
}

public sealed class CoverFace
{
    public string? Name { get; set; }
    public double DistanceIn { get; set; }
}

public sealed class LocalBasisInfo
{
    public double[] OriginFt { get; set; } = [];
    public double[] XDir { get; set; } = [];
    public double[] YDir { get; set; } = [];
    public double XWorldDeg { get; set; }
}

public sealed class BboxInfo
{
    public double[] MinFt { get; set; } = [];
    public double[] MaxFt { get; set; } = [];
}

public sealed class BoundaryEdgeInfo
{
    public int Index { get; set; }
    public string Kind { get; set; } = "line";          // geometry kind (line; arc in Phase 5)
    public double[] StartFt { get; set; } = [];
    public double[] EndFt { get; set; } = [];
    public double LengthFt { get; set; }
    public double MidNormalWorldDeg { get; set; }
    public string Edge { get; set; } = "free";          // free | beam | wall | slab
    public AdjacentInfo? Adjacent { get; set; }
}

public sealed class AdjacentInfo
{
    public string Kind { get; set; } = "";              // Beam | Wall | Slab
    public long ElementId { get; set; }
    public string? Mark { get; set; }
}

public sealed class OpeningInfo
{
    public int Id { get; set; }
    public string Source { get; set; } = "";            // Void (geometry hole)
    public string Class { get; set; } = "";             // Trim | Shaft | EdgeAdjacent
    public string ClassReason { get; set; } = "";
    public double AreaSf { get; set; }
    public bool NeedsTrim { get; set; }
    public BboxInfo Bbox { get; set; } = new();
    public List<EdgeSegmentInfo> Boundary { get; set; } = [];
}

public sealed class EdgeSegmentInfo
{
    public int Index { get; set; }
    public double[] StartFt { get; set; } = [];
    public double[] EndFt { get; set; } = [];
    public double LengthFt { get; set; }
}

public sealed class ContextInfo
{
    public List<SupportInfo> SupportsBelow { get; set; } = [];
    public List<NeighborGroupInfo> WallsBounding { get; set; } = [];
    public List<NeighborGroupInfo> Beams { get; set; } = [];
    public List<NeighborGroupInfo> SlabsCoplanar { get; set; } = [];
    public NeighborSlabInfo? SlabAbove { get; set; }    // not computed yet (PR-05+/Phase 5)
    public NeighborSlabInfo? SlabBelow { get; set; }
}

public sealed class SupportInfo
{
    public string Kind { get; set; } = "";              // Column | Wall | Beam
    public long ElementId { get; set; }
    public string? Mark { get; set; }
    public double[] CenterFt { get; set; } = [];
    public double WidthIn { get; set; }
    public double DepthIn { get; set; }
    /// <summary>Plan centerline [ax, ay, bx, by] (ft) for walls / beams; null for columns.
    /// Anchor wall strips (e.g. shaft-core faces) to this, not just the center point.</summary>
    public double[]? LineFt { get; set; }
}

public sealed class NeighborGroupInfo
{
    public long ElementId { get; set; }
    public string? Mark { get; set; }
    public List<int> BoundaryIndices { get; set; } = [];
}

public sealed class NeighborSlabInfo
{
    public long ElementId { get; set; }
    public string? Mark { get; set; }
    public double ThicknessIn { get; set; }
}

public sealed class HintsInfo
{
    public List<int> FreeEdgeIndices { get; set; } = [];
    public bool NeedsEdgeUBars { get; set; }
    public List<int> OpeningsNeedTrim { get; set; } = [];
    public List<SupportHintInfo> Supports { get; set; } = [];
    public double MaxSpanFt { get; set; }
    public bool IsTwoWay { get; set; }
    public List<string> RecommendedLayers { get; set; } = [];
}

public sealed class SupportHintInfo
{
    public string? Mark { get; set; }
    public double SuggestedStripWidthIn { get; set; }
}
