namespace StairsReinforcement.Geometry;

/// <summary>Mutable axis-aligned 3-D bounds accumulator (feet).</summary>
public sealed class Bounds3
{
    public Pt3 Min { get; private set; }
    public Pt3 Max { get; private set; }
    public bool IsEmpty { get; private set; } = true;

    public void Add(Pt3 p)
    {
        if (IsEmpty) { Min = p; Max = p; IsEmpty = false; return; }
        Min = new Pt3(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z));
        Max = new Pt3(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z));
    }

    public Pt3 Center => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2, (Min.Z + Max.Z) / 2);
    public Pt3 Size => Max - Min;
}
