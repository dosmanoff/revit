namespace StairsReinforcement.Geometry;

/// <summary>Immutable 3-D point/vector (feet). Revit-free; mirrors the parts of XYZ the math needs.</summary>
public readonly struct Pt3(double x, double y, double z)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Z { get; } = z;

    public static Pt3 operator +(Pt3 a, Pt3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Pt3 operator -(Pt3 a, Pt3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Pt3 operator *(Pt3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double Dot(Pt3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public Pt3 Cross(Pt3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

    public Pt3 Normalized()
    {
        double len = Length;
        return len < 1e-12 ? new Pt3(0, 0, 0) : new Pt3(X / len, Y / len, Z / len);
    }

    public Pt2 ToPlan() => new(X, Y);

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
