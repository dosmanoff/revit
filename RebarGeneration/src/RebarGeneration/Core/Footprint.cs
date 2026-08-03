using Autodesk.Revit.DB;
using RebarGeneration.Contracts;
using SlabReinforcement.Geometry;

namespace RebarGeneration.Core;

/// <summary>
/// Реальный контур хоста в плане: внешний контур плюс отверстия.
/// <para>
/// Зачем: раскладка по габаритному боксу считает подошву прямоугольной и
/// выровненной по осям. На настоящей геометрии это даёт стержни, торчащие из
/// бетона, — ровно это и наблюдалось при проверке (16 предупреждений «Rebar is
/// placed completely outside of its host» на L-образной плите). Контур снимается
/// с нижней горизонтальной грани солида, а дальше раскладку считает уже
/// оттестированный <c>FieldLayout</c>.
/// </para>
/// </summary>
public sealed class Footprint
{
    public required Loop2 Outer { get; init; }
    public required IReadOnlyList<Loop2> Holes { get; init; }

    /// <summary>Отметка нижней грани, футы.</summary>
    public required double BottomZ { get; init; }

    /// <summary>Отметка верхней грани, футы.</summary>
    public required double TopZ { get; init; }

    /// <summary>
    /// Снять контур с элемента. Возвращает <c>null</c>, если геометрия не даёт
    /// однозначной нижней грани — вызывающий откатывается на габарит и говорит
    /// об этом в отчёте, а не молча считает неправильно.
    /// </summary>
    public static Footprint? TryExtract(Element host)
    {
        var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Medium };
        GeometryElement? geom = host.get_Geometry(opt);
        if (geom is null) return null;

        PlanarFace? bottom = null;
        double bestArea = 0;

        foreach (Solid s in EnumerateSolids(geom))
        foreach (Face f in s.Faces)
        {
            if (f is not PlanarFace pf) continue;
            // Нижняя грань: нормаль смотрит вниз. Допуск щедрый — грань может
            // быть слегка непараллельна из-за уклона.
            if (pf.FaceNormal.Z > -0.99) continue;
            double area = pf.Area;
            if (area > bestArea) { bestArea = area; bottom = pf; }
        }

        if (bottom is null) return null;

        IList<CurveLoop> loops;
        try { loops = bottom.GetEdgesAsCurveLoops(); }
        catch { return null; }
        if (loops.Count == 0) return null;

        var polys = new List<(Loop2 Loop, double Area)>();
        foreach (CurveLoop cl in loops)
        {
            Loop2? p = ToLoop(cl);
            if (p is not null) polys.Add((p, p.Area));
        }
        if (polys.Count == 0) return null;

        polys.Sort((a, b) => b.Area.CompareTo(a.Area));
        Loop2 outer = polys[0].Loop;
        var holes = polys.Skip(1).Select(p => p.Loop).ToList();

        BoundingBoxXYZ? bb = host.get_BoundingBox(null);
        double bottomZ = bottom.Origin.Z;
        double topZ = bb?.Max.Z ?? bottomZ;

        return new Footprint { Outer = outer, Holes = holes, BottomZ = bottomZ, TopZ = topZ };
    }

    private static IEnumerable<Solid> EnumerateSolids(GeometryElement geom)
    {
        foreach (GeometryObject o in geom)
        {
            switch (o)
            {
                case Solid s when s.Volume > 1e-9:
                    yield return s;
                    break;
                case GeometryInstance gi:
                    // Семейства (например отдельно стоящий фундамент) отдают
                    // геометрию через экземпляр — без этого солидов просто нет.
                    GeometryElement inst = gi.GetInstanceGeometry();
                    foreach (Solid s2 in EnumerateSolids(inst)) yield return s2;
                    break;
            }
        }
    }

    private static Loop2? ToLoop(CurveLoop cl)
    {
        var pts = new List<Pt2>();
        foreach (Curve c in cl)
            // Tessellate разложит и дуги: контур подошвы бывает скруглённым.
            foreach (XYZ p in c.Tessellate())
            {
                var q = new Pt2(p.X, p.Y);
                if (pts.Count == 0 || Far(pts[^1], q)) pts.Add(q);
            }

        // Замыкающая точка совпадает с первой — убираем, Loop2 её не ждёт.
        if (pts.Count > 1 && !Far(pts[0], pts[^1])) pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 3) return null;

        try { return new Loop2(pts); }
        catch (ArgumentException) { return null; }
    }

    private static bool Far(Pt2 a, Pt2 b) =>
        Math.Abs(a.X - b.X) > 1e-7 || Math.Abs(a.Y - b.Y) > 1e-7;

    /// <summary>Из локальной системы стержня обратно в мир (поворот на угол
    /// направления). Обратная к тому, что <c>FieldLayout</c> делает внутри.</summary>
    public static Vec3 ToWorld(double localX, double localY, double cos, double sin, double z) =>
        new(localX * cos - localY * sin, localX * sin + localY * cos, z);

    /// <summary>Габарит контура — для сообщений и отладки.</summary>
    public string Describe() =>
        $"контур {Outer.Points.Count} вершин, отверстий {Holes.Count}, "
        + $"площадь {Outer.Area:0.#} ft², отметки {BottomZ:0.##}…{TopZ:0.##} ft";
}
