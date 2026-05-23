namespace RevitPlugin.Domain.Geometry;

/// <summary>
/// Прямолинейный сегмент в 3D-пространстве (мм).
/// </summary>
public readonly record struct Line3(Vec3 Start, Vec3 End)
{
    public Vec3 Direction => (End - Start).Normalized();
    public double Length => (End - Start).Length;
}
