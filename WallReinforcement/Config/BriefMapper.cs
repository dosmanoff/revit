namespace WallReinforcement.Config;

/// <summary>
/// Maps a <see cref="BriefWall"/> to the <see cref="ReinforcementConfig"/> the builders consume.
/// A brief entry is essentially a per-wall config minus the document-level name, so this is a
/// straight section-by-section copy (the brief reuses the very same section POCOs). Mirrors
/// <c>SlabReinforcement.Config.BriefMapper</c>.
/// </summary>
public static class BriefMapper
{
    public static ReinforcementConfig ToConfig(WallBrief brief, BriefWall w) => new()
    {
        Name = !string.IsNullOrWhiteSpace(w.Mark) ? w.Mark! : $"brief-{w.ElementId}",
        Units = brief.Units,
        Cover = w.Cover,
        FaceMesh = w.FaceMesh,
        Openings = w.Openings,
        Edges = w.Edges,
        Ties = w.Ties,
        Corners = w.Corners,
        TJunctions = w.TJunctions,
        Anchorage = w.Anchorage,
    };
}
