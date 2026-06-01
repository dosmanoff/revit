using SlabReinforcement.Config;

namespace SlabReinforcement.Domain;

public enum ZoneFace { Top, Bottom }
public enum ZoneAxis { X, Y }

/// <summary>How a strengthening zone's plan extent is described.</summary>
public enum ZoneShapeKind
{
    /// <summary>A band of width <see cref="ZoneSpec.StripWidth"/> centred on a named support.</summary>
    SupportMark,
    /// <summary>An axis-aligned rectangle: <see cref="ZoneSpec.Coords"/> = [x1, y1, x2, y2] (feet).</summary>
    BBox,
    /// <summary>A polygon: <see cref="ZoneSpec.Coords"/> = [x, y, x, y, …] (feet).</summary>
    Polygon,
}

/// <summary>One top/bottom strengthening zone from <c>slab-zones.csv</c>.</summary>
public sealed class ZoneSpec
{
    public required string SlabMark { get; init; }
    public string ZoneName { get; init; } = "";
    public ZoneFace Face { get; init; } = ZoneFace.Top;
    public ZoneAxis Axis { get; init; } = ZoneAxis.X;
    public required string BarType { get; init; }
    public Length Spacing { get; init; } = new(12);

    public ZoneShapeKind ShapeKind { get; init; }
    public string? SupportMark { get; init; }       // ShapeKind == SupportMark
    public Length StripWidth { get; init; } = new(0);
    public double[] Coords { get; init; } = [];      // ShapeKind == BBox | Polygon (feet)
    public Length Extent { get; init; } = new(0);    // how far past the support face
}

public sealed class ZoneParseResult
{
    public IReadOnlyList<ZoneSpec> Zones { get; init; } = [];
    public IReadOnlyList<ParseIssue> Issues { get; init; } = [];
}
