namespace RevitPlugin.Domain.Geometry;

/// <summary>
/// Прямоугольный проём в стене в системе координат стены
/// (X — вдоль location line, Y — по высоте от низа стены).
/// </summary>
public sealed record OpeningGeometry(
    string Id,
    double XStart,
    double XEnd,
    double YStart,
    double YEnd,
    OpeningKind Kind)
{
    public double Width => XEnd - XStart;
    public double Height => YEnd - YStart;
}

public enum OpeningKind
{
    Generic,
    Door,
    Window
}
