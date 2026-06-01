namespace SlabReinforcement.Domain;

/// <summary>
/// Reinforcement layer of a generated bar. Written into the bar's Comments tag
/// (<c>SR:{config}:{slabId}:{layer}</c>) so Slab Views can isolate it and re-runs
/// can clean it. The four mesh layers map to Slab Views' Layer 1–4.
/// </summary>
public enum SlabLayer
{
    BottomX,
    BottomY,
    TopX,
    TopY,
    EdgeU,
    OpeningTrim,
    Support,
    Dowel,
}
