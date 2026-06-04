using SlabReinforcement.Geometry;

namespace SlabReinforcement.Domain;

public enum OpeningSource { Void }

/// <summary>A penetration through the slab (a hole in the concrete), with its plan boundary and
/// a trim flag.</summary>
public sealed class SlabOpening
{
    public required OpeningSource Source { get; init; }
    public required Loop2 Boundary { get; init; }      // world XY, feet
    public required bool NeedsTrim { get; init; }

    public Bounds2 Bounds => Boundary.Bounds;
    public double AreaSf => Boundary.Area;
}

/// <summary>
/// The slab's openings, taken from <see cref="SlabGeometry.Openings"/> — i.e. the holes in the
/// real slab face, captured regardless of how they were modelled (sketch cut-out, Opening/shaft
/// element, or family void cut). Flags <see cref="SlabOpening.NeedsTrim"/> by plan size.
/// </summary>
public static class SlabOpenings
{
    /// <summary>Openings this size (either plan dimension) or larger get trim bars / U-bars + diagonals.</summary>
    public const double TrimThresholdFt = 1.0;   // 12"

    public static IReadOnlyList<SlabOpening> For(SlabGeometry geom)
    {
        var result = new List<SlabOpening>(geom.Openings.Count);
        foreach (Loop2 loop in geom.Openings)
            result.Add(new SlabOpening
            {
                Source = OpeningSource.Void,
                Boundary = loop,
                NeedsTrim = Math.Max(loop.Bounds.Width, loop.Bounds.Height) >= TrimThresholdFt,
            });
        return result;
    }

    public static int TrimCount(IReadOnlyList<SlabOpening> openings) => openings.Count(o => o.NeedsTrim);
}
