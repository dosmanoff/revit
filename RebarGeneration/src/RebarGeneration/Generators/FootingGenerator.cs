using Autodesk.Revit.DB;
using RebarGeneration.Contracts;
using RebarGeneration.Core;

namespace RebarGeneration.Generators;

/// <summary>
/// Сетки подошвы фундамента — тип хоста, для которого своего плагина в репозитории
/// нет, а объём работы регулярный.
/// <para>
/// Строит нижнюю и (при необходимости) верхнюю сетку по габариту хоста с учётом
/// защитного слоя, укладывая слои друг на друга по фактическим диаметрам:
/// нижний X, поверх него нижний Y; сверху — зеркально. Каждое направление —
/// ОДИН набор, а не N стержней.
/// </para>
/// <para><b>Ограничение, о котором надо знать.</b> Раскладка идёт по габаритному
/// боксу в координатах модели, то есть предполагает прямоугольную подошву,
/// выровненную по осям. Повёрнутый или непрямоугольный фундамент так армировать
/// нельзя — для него используется генератор <c>polyline</c>, где геометрию задаёт
/// вызывающая сторона. Проверка на это здесь намеренно не делается: габарит
/// повёрнутого элемента внешне выглядит корректным, и «умная» эвристика тут
/// врала бы чаще, чем помогала.</para>
/// </summary>
public sealed class FootingGenerator : IRebarGenerator
{
    public string Name => "footing";

    public IReadOnlyList<PlannedSet> Plan(GroupSpec g, BuildContext ctx)
    {
        if (g.Bottom is null && g.Top is null)
            throw new JobException("NO_MAT", $"{g.Key}: generator=footing требует bottom и/или top");

        BoundingBoxXYZ box = ctx.Box;
        double cover = ctx.CoverFt(g);

        double spanX = box.Max.X - box.Min.X - 2 * cover;
        double spanY = box.Max.Y - box.Min.Y - 2 * cover;
        if (spanX <= 0 || spanY <= 0)
            throw new JobException("COVER_TOO_BIG",
                $"{g.Key}: защитный слой {cover:0.###} ft не оставляет места внутри подошвы");

        bool hooked = string.Equals(g.Edge, "hook", StringComparison.OrdinalIgnoreCase);
        (string? hookStart, string? hookEnd) = ctx.HooksOf(g);
        if (!hooked) (hookStart, hookEnd) = (null, null);

        var plan = new List<PlannedSet>(4);

        if (g.Bottom is not null)
            AddMat(plan, g, ctx, box, cover, g.Bottom, fromBottom: true, hookStart, hookEnd);

        if (g.Top is not null)
            AddMat(plan, g, ctx, box, cover, g.Top, fromBottom: false, hookStart, hookEnd);

        if (plan.Count == 0)
            throw new JobException("EMPTY_MAT", $"{g.Key}: в bottom/top нет ни x, ни y");

        return plan;
    }

    private static void AddMat(
        List<PlannedSet> plan, GroupSpec g, BuildContext ctx, BoundingBoxXYZ box,
        double cover, MatSpec mat, bool fromBottom, string? hookStart, string? hookEnd)
    {
        // Порядок слоёв: ближний к грани идёт первым и лежит ниже (сверху — выше).
        // Диаметры настоящие, из типа стержня: сетка из #8 поверх #4 иначе уедет
        // в бетон или наружу.
        DirSpec? first = mat.X ?? mat.Y;
        DirSpec? second = mat.X is not null ? mat.Y : null;
        if (first is null) return;

        string firstBar = ctx.BarTypeOf(g, first.BarType);
        double dFirst = ctx.Types.DiameterFt(firstBar);

        double face = fromBottom ? box.Min.Z + cover : box.Max.Z - cover;
        double sign = fromBottom ? +1 : -1;

        double zFirst = face + sign * (dFirst / 2.0);
        bool firstIsX = mat.X is not null;
        AddLayer(plan, g, ctx, box, cover, first, firstBar, zFirst, firstIsX,
            fromBottom ? "bot" : "top", hookStart, hookEnd);

        if (second is null) return;

        string secondBar = ctx.BarTypeOf(g, second.BarType);
        double dSecond = ctx.Types.DiameterFt(secondBar);
        double zSecond = face + sign * (dFirst + dSecond / 2.0);
        AddLayer(plan, g, ctx, box, cover, second, secondBar, zSecond, alongX: false,
            fromBottom ? "bot" : "top", hookStart, hookEnd);
    }

    private static void AddLayer(
        List<PlannedSet> plan, GroupSpec g, BuildContext ctx, BoundingBoxXYZ box,
        double cover, DirSpec spec, string barType, double z, bool alongX,
        string zone, string? hookStart, string? hookEnd)
    {
        double desired = ctx.U.Ft(spec.Spacing);

        // Стержень тянется вдоль своей оси, а размножается вдоль перпендикулярной.
        double barFrom = alongX ? box.Min.X + cover : box.Min.Y + cover;
        double barTo = alongX ? box.Max.X - cover : box.Max.Y - cover;

        double distMin = alongX ? box.Min.Y : box.Min.X;
        double distMax = alongX ? box.Max.Y : box.Max.X;

        (int count, double spacing, double firstPos) =
            LayoutMath.WithinCover(distMin, distMax, cover, desired);

        if (count <= 0)
            throw new JobException("EMPTY_LAYER",
                $"{g.Key}: слой {zone}.{(alongX ? "x" : "y")} не поместился при шаге {spec.Spacing}");

        Vec3 a = alongX ? new Vec3(barFrom, firstPos, z) : new Vec3(firstPos, barFrom, z);
        Vec3 b = alongX ? new Vec3(barTo, firstPos, z) : new Vec3(firstPos, barTo, z);

        plan.Add(new PlannedSet(
            Polyline: [a, b],
            BarType: barType,
            Count: count,
            SpacingFt: spacing,
            // Раскладка строго в плоскости подошвы: без явного направления Revit
            // получил бы вертикальную нормаль прямого горизонтального стержня и
            // разложил бы набор вверх.
            Distribution: alongX ? Vec3.BasisY : Vec3.BasisX,
            HookStart: hookStart,
            HookEnd: hookEnd,
            Suffix: $"{zone}{(alongX ? "x" : "y")}"));
    }
}
