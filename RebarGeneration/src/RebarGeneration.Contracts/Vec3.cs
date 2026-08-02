namespace RebarGeneration.Contracts;

/// <summary>
/// Точка/вектор в футах. Существует, чтобы вся геометрическая математика
/// (нормали, раскладка) считалась и тестировалась БЕЗ Revit — <c>XYZ</c>
/// появляется только на границе с API.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 BasisX = new(1, 0, 0);
    public static readonly Vec3 BasisY = new(0, 1, 0);
    public static readonly Vec3 BasisZ = new(0, 0, 1);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vec3 Cross(Vec3 o) =>
        new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

    public double DistanceTo(Vec3 o) => (this - o).Length;

    /// <summary>Единичный вектор. Нулевой вектор возвращается как есть — вызывающий
    /// код всегда сначала проверяет длину, а бросать отсюда нечего.</summary>
    public Vec3 Normalized()
    {
        double l = Length;
        return l < 1e-12 ? this : new Vec3(X / l, Y / l, Z / l);
    }

    public double[] ToArray() => [X, Y, Z];

    public override string ToString() => $"({X:0.####}, {Y:0.####}, {Z:0.####})";
}
