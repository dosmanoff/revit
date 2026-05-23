namespace RevitPlugin.Domain.Geometry;

/// <summary>
/// Чистое доменное представление стены — то, что нужно правилам для расчёта арматуры.
/// Адаптер (<c>WallRepository</c>) собирает этот объект из <c>Autodesk.Revit.DB.Wall</c>.
/// </summary>
/// <remarks>
/// Координаты — в миллиметрах. Стена выпрямлена в локальную систему:
/// X — вдоль <see cref="LocationLine"/> от Start к End, Y — по высоте от низа стены,
/// Z — наружу из внешней грани.
/// </remarks>
public sealed record WallContext(
    long Id,
    string Mark,
    string TypeName,
    Line3 LocationLine,
    double Height,
    double Thickness,
    double BaseElevation,
    Vec3 ExteriorNormal,
    IReadOnlyList<OpeningGeometry> Openings,
    IReadOnlyDictionary<string, string> Parameters)
{
    public double Length => LocationLine.Length;

    /// <summary>Создаёт прямую стену без проёмов — удобный фикстур-конструктор для тестов.</summary>
    public static WallContext CreateStraight(
        double length,
        double height,
        double thickness,
        long id = 1,
        string mark = "СТ-1") =>
        new(
            Id: id,
            Mark: mark,
            TypeName: "Generic - " + thickness + "mm",
            LocationLine: new Line3(Vec3.Zero, new Vec3(length, 0, 0)),
            Height: height,
            Thickness: thickness,
            BaseElevation: 0,
            ExteriorNormal: Vec3.UnitY,
            Openings: Array.Empty<OpeningGeometry>(),
            Parameters: new Dictionary<string, string>());
}
