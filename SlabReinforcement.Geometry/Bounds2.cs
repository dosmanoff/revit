namespace SlabReinforcement.Geometry;

/// <summary>Axis-aligned bounding rectangle in plan (feet).</summary>
public readonly struct Bounds2
{
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public Bounds2(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
    }

    public double Width  => MaxX - MinX;
    public double Height => MaxY - MinY;

    public static Bounds2 Of(IEnumerable<Pt2> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;

        foreach (Pt2 p in points)
        {
            any = true;
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        return any ? new Bounds2(minX, minY, maxX, maxY) : new Bounds2(0, 0, 0, 0);
    }
}
