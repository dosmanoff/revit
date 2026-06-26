using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallReinforcement.Export;

/// <summary>
/// Serializable description of one or more walls for the external reinforcement agent. Property
/// names serialize to snake_case (see <see cref="JsonOptions"/>) to match the SlabReinforcement /
/// ColumnReinforcement dump convention. Schema documented in wall-dump-schema.md.
///
/// A wall is a rectangular panel (length u × height v) of a given thickness, with openings (in
/// wall u/v coords) and L-corner / T-stem junctions to neighbouring walls. The agent turns this
/// into a <c>WallBrief</c> (see wall-brief-schema.md).
/// </summary>
public sealed class WallDump
{
    public DocumentInfo Document { get; set; } = new();
    public string GeneratedAt { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string UnitsNote { get; set; } =
        "Thickness, cover, bar diameters and opening sill/head are in inches; lengths, heights, " +
        "elevations and plan coords in feet (Revit internal). Angles in degrees from world +X. " +
        "Wall (u,v): u runs along the wall length from its start end, v upward from the base.";

    public List<LevelInfo> Levels { get; set; } = [];
    public Dictionary<string, WallTypeInfo> WallTypesInUse { get; set; } = [];
    public List<BarTypeInfo> AvailableRebarBarTypes { get; set; } = [];
    public List<HookTypeInfo> AvailableRebarHookTypes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<WallInfo> Walls { get; set; } = [];

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

public sealed class WallTypeInfo
{
    public string Family { get; set; } = "";
    public string Type { get; set; } = "";
    public long Id { get; set; }
    public double ThicknessIn { get; set; }
    public string Function { get; set; } = "";        // Exterior | Interior | Foundation | Retaining | Soffit | CoreShaft
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

public sealed class WallInfo
{
    public long ElementId { get; set; }
    public string? Mark { get; set; }
    public string? Comments { get; set; }
    public string Family { get; set; } = "";
    public string Type { get; set; } = "";
    public long TypeId { get; set; }

    public LevelRef? BaseLevel { get; set; }
    public LevelRef? TopLevel { get; set; }
    public double ThicknessIn { get; set; }
    public double LengthFt { get; set; }
    public double HeightFt { get; set; }
    public double BaseElevationFt { get; set; }
    public double TopElevationFt { get; set; }

    public string StructuralUsage { get; set; } = "";  // Bearing | Shear | Combined | NonBearing
    public string Function { get; set; } = "";
    public bool IsStructural { get; set; }
    public bool IsArc { get; set; }
    public bool Flipped { get; set; }

    public CoverInfo RebarCover { get; set; } = new();
    public LocalBasisInfo LocalBasis { get; set; } = new();
    public BboxInfo Bbox { get; set; } = new();
    public List<FaceInfo> Faces { get; set; } = [];

    public List<OpeningInfo> Openings { get; set; } = [];
    public List<JunctionInfo> Junctions { get; set; } = [];
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
    public CoverFace? Exterior { get; set; }
    public CoverFace? Interior { get; set; }
    public CoverFace? Other { get; set; }              // ends / top / bottom faces
}

public sealed class CoverFace
{
    public string? Name { get; set; }
    public double DistanceIn { get; set; }
}

public sealed class LocalBasisInfo
{
    public double[] OriginFt { get; set; } = [];       // base corner at the start end, bottom of wall [x,y,z]
    public double[] LengthDir { get; set; } = [];      // along the LocationCurve [x,y]
    public double[] NormalDir { get; set; } = [];      // wall facing direction (interior→exterior) [x,y]
    public double LengthWorldDeg { get; set; }
}

public sealed class BboxInfo
{
    public double[] MinFt { get; set; } = [];
    public double[] MaxFt { get; set; } = [];
}

public sealed class FaceInfo
{
    public string Side { get; set; } = "";             // exterior | interior
    public double GrossAreaSf { get; set; }            // length × height
    public double NetAreaSf { get; set; }              // less openings
}

public sealed class OpeningInfo
{
    public int Id { get; set; }                        // 1-based
    public long InsertId { get; set; }                 // hosting door/window/opening element id
    public string? Category { get; set; }              // Doors | Windows | Wall Openings | …
    public string? Family { get; set; }
    public string? Type { get; set; }
    public double UMinFt { get; set; }
    public double UMaxFt { get; set; }
    public double VMinFt { get; set; }
    public double VMaxFt { get; set; }
    public double WidthFt { get; set; }
    public double HeightFt { get; set; }
    public double SillFt { get; set; }                 // = v_min, above the wall base
    public double HeadFt { get; set; }                 // = v_max
    public bool NeedsTrim { get; set; }
    public BboxInfo Bbox { get; set; } = new();        // world AABB
}

public sealed class JunctionInfo
{
    public string Kind { get; set; } = "";             // LCorner | TStem
    public string AtEnd { get; set; } = "";            // start | end (which end of THIS wall)
    public double OurUFt { get; set; }                 // 0 or length
    public long OtherWallId { get; set; }
    public string? OtherWallMark { get; set; }
    public double[] PointFt { get; set; } = [];        // world joint point [x,y]
    public double[] OtherDir { get; set; } = [];       // unit vector along the other wall, away from the joint [x,y]
}

public sealed class HintsInfo
{
    public List<string> RecommendedFaces { get; set; } = [];
    public bool NeedsOpeningTrim { get; set; }
    public List<int> OpeningsNeedTrim { get; set; } = [];
    public bool HasCorners { get; set; }
    public bool HasTJunctions { get; set; }
    public bool ThickEnoughForTies { get; set; }
    public List<string> RecommendedLayers { get; set; } = [];
}
