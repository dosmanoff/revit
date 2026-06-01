namespace SlabReinforcement.Geometry;

/// <summary>
/// A closed polygon loop in plan, given by its distinct vertices (no repeated
/// closing point). Edge <c>i</c> runs from <c>Points[i]</c> to <c>Points[i+1]</c>,
/// with the last edge wrapping back to <c>Points[0]</c>.
/// </summary>
public sealed class Loop2
{
    public IReadOnlyList<Pt2> Points { get; }

    public Loop2(IReadOnlyList<Pt2> points)
    {
        if (points.Count < 3)
            throw new ArgumentException("A loop needs at least 3 vertices.", nameof(points));
        Points = points;
    }

    /// <summary>Signed area (shoelace): positive for counter-clockwise winding.</summary>
    public double SignedArea => GeometryMath.SignedArea(Points);

    /// <summary>Absolute enclosed area (ft²).</summary>
    public double Area => Math.Abs(SignedArea);

    public Bounds2 Bounds => Bounds2.Of(Points);
}
