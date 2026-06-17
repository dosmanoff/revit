namespace StairsReinforcement.Geometry;

/// <summary>Immutable 2-D point/vector (feet). Revit-free so the math is unit-testable.</summary>
public readonly struct Pt2(double x, double y)
{
    public double X { get; } = x;
    public double Y { get; } = y;

    public static Pt2 operator +(Pt2 a, Pt2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt2 operator -(Pt2 a, Pt2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt2 operator *(Pt2 a, double s) => new(a.X * s, a.Y * s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dot(Pt2 o) => X * o.X + Y * o.Y;
    public double Cross(Pt2 o) => X * o.Y - Y * o.X;

    public Pt2 Normalized()
    {
        double len = Length;
        return len < 1e-12 ? new Pt2(0, 0) : new Pt2(X / len, Y / len);
    }

    /// <summary>90° CCW perpendicular.</summary>
    public Pt2 Perp() => new(-Y, X);

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
