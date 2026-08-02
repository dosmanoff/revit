namespace RebarGeneration.Contracts;

/// <summary>
/// Нормаль плоскости стержня для <c>Rebar.CreateFromCurves</c>.
/// <para>
/// Грабли, за которые уже заплачено: <c>CreateFromCurves</c> требует ИСТИННУЮ
/// нормаль плоскости, содержащей кривые. Захардкоженный <c>BasisZ</c> ломает
/// стержни в вертикальных плоскостях (например, стержень плиты, ныряющий под
/// приямок) — Revit либо разворачивает стержень, либо отказывается его строить.
/// </para>
/// <para>
/// Логика намеренно совпадает с <c>revit_context/actions/ops.py:_plane_normal</c>,
/// включая частный случай прямого горизонтального стержня: для него сохраняется
/// исторический <c>BasisZ</c>, иначе у ранее работавших стержней молча поехала бы
/// плоскость загиба крюка.
/// </para>
/// </summary>
public static class PlaneNormal
{
    private const double DirEps = 1e-9;
    private const double CrossEps = 1e-6;

    /// <summary>Нормаль по списку сегментов (каждый — пара точек, футы).</summary>
    public static Vec3 FromSegments(IReadOnlyList<(Vec3 A, Vec3 B)> segments)
    {
        var dirs = new List<Vec3>();
        foreach ((Vec3 a, Vec3 b) in segments)
        {
            Vec3 d = b - a;
            if (d.Length > DirEps) dirs.Add(d.Normalized());
        }

        // Две непараллельные касательные однозначно задают плоскость.
        for (int i = 0; i < dirs.Count; i++)
        for (int j = i + 1; j < dirs.Count; j++)
        {
            Vec3 n = dirs[i].Cross(dirs[j]);
            if (n.Length > CrossEps) return n.Normalized();
        }

        if (dirs.Count == 0) return Vec3.BasisZ;                 // вырожденный ввод

        // Все сегменты коллинеарны — плоскость не определена однозначно.
        Vec3 d0 = dirs[0];
        if (Math.Abs(d0.Z) < CrossEps) return Vec3.BasisZ;       // горизонтальный: как раньше

        Vec3 normal = d0.Cross(Vec3.BasisZ);
        if (normal.Length < CrossEps) normal = d0.Cross(Vec3.BasisX);
        return normal.Normalized();
    }

    /// <summary>Нормаль по полилинии (последовательность точек, футы).</summary>
    public static Vec3 FromPolyline(IReadOnlyList<Vec3> points)
    {
        var segs = new List<(Vec3, Vec3)>(Math.Max(0, points.Count - 1));
        for (int i = 0; i + 1 < points.Count; i++) segs.Add((points[i], points[i + 1]));
        return FromSegments(segs);
    }
}
