namespace SlabReinforcement.Geometry;

/// <summary>A directed 2-D segment in plan (feet).</summary>
public readonly struct Seg2
{
    public Pt2 A { get; }
    public Pt2 B { get; }

    public Seg2(Pt2 a, Pt2 b) { A = a; B = b; }

    public Pt2 Delta => B - A;
    public double Length => Delta.Length;
    public Pt2 Dir => Delta.Normalized;
    public Pt2 Mid => new((A.X + B.X) * 0.5, (A.Y + B.Y) * 0.5);

    public override string ToString() => $"{A}->{B}";
}
