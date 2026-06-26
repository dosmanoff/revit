namespace WallReinforcement.Config;

/// <summary>
/// The structured JSON reinforcement brief produced by the agent (from the wall dump + the
/// project's reinforcement task) and consumed by Wall Reinforcement. One entry per wall, matched
/// by <see cref="BriefWall.Mark"/> or <see cref="BriefWall.ElementId"/>, each carrying the full
/// per-wall reinforcement spec. Mirrors <c>SlabBrief</c>; reuses the same section POCOs the
/// single-config flow uses, so a brief entry is just a per-wall <see cref="ReinforcementConfig"/>
/// minus the document-level <c>name</c>. Schema documented in wall-brief-schema.md.
/// </summary>
public sealed class WallBrief
{
    public int SchemaVersion { get; set; } = 1;
    public UnitSystem Units { get; set; } = UnitSystem.Imperial;
    public List<BriefWall> Walls { get; set; } = [];
}

public sealed class BriefWall
{
    public string? Mark { get; set; }                  // match the wall by Mark…
    public long ElementId { get; set; }                // …or by element id (0 = unused)
    public bool CleanExisting { get; set; } = true;

    public CoverConfig Cover { get; set; } = new();
    public FaceMeshConfig FaceMesh { get; set; } = new();
    public OpeningsConfig Openings { get; set; } = new();
    public EdgesConfig Edges { get; set; } = new();
    public TiesConfig Ties { get; set; } = new();
    public CornersConfig Corners { get; set; } = new();
    public TJunctionsConfig TJunctions { get; set; } = new();
    public AnchorageConfig Anchorage { get; set; } = new();
}
