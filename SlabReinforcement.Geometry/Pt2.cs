namespace SlabReinforcement.Geometry;

/// <summary>An immutable 2-D point/vector in plan (world XY, Revit internal feet).</summary>
public readonly struct Pt2
{
    public double X { get; }
    public double Y { get; }

    public Pt2(double x, double y) { X = x; Y = y; }

    public static Pt2 operator +(Pt2 a, Pt2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt2 operator -(Pt2 a, Pt2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt2 operator *(Pt2 a, double s) => new(a.X * s, a.Y * s);

    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>Unit vector; the zero vector maps to itself.</summary>
    public Pt2 Normalized
    {
        get
        {
            double len = Length;
            return len < 1e-12 ? new Pt2(0, 0) : new Pt2(X / len, Y / len);
        }
    }

    public double Dot(Pt2 o) => X * o.X + Y * o.Y;

    /// <summary>2-D cross product (z component of the 3-D cross).</summary>
    public double Cross(Pt2 o) => X * o.Y - Y * o.X;

    /// <summary>90° counter-clockwise rotation — equivalent to BasisZ × this in 3-D.</summary>
    public Pt2 Perp => new(-Y, X);

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
