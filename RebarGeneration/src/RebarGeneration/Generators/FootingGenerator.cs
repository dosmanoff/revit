using Autodesk.Revit.DB;
using RebarGeneration.Contracts;
using RebarGeneration.Core;
using SlabReinforcement.Geometry;

namespace RebarGeneration.Generators;

/// <summary>
/// Сетки подошвы фундамента — тип хоста, для которого своего плагина в
/// репозитории нет, а объём работы регулярный.
/// <para>
/// По умолчанию раскладка идёт по <b>реальному контуру</b> хоста: контур
/// снимается с нижней грани солида, а сканлайн-клиппинг и группировку в полосы
/// считает оттестированный <see cref="FieldLayout"/> из
/// <c>SlabReinforcement.Geometry</c>. Поэтому L-образная подошва, вырезы и
/// отверстия обрабатываются правильно, а каждая полоса равных стержней ложится
/// ОДНИМ набором.
/// </para>
/// <para>
/// Если контур снять не удалось (нестандартная геометрия, нет однозначной нижней
/// грани), генератор откатывается на габаритный бокс и <b>говорит об этом</b> в
/// отчёте: молча считать по боксу и выдавать это за раскладку по контуру нельзя.
/// Повёрнутая подошва задаётся углом <c>angle</c>.
/// </para>
/// </summary>
public sealed class FootingGenerator : IRebarGenerator
{
    public string Name => "footing";

    /// <summary>Заполняется при построении, забирается раннером в предупреждения.</summary>
    public static readonly string FallbackNote =
        "контур не снялся — раскладка по габаритному боксу (для повёрнутой или "
        + "непрямоугольной подошвы результат будет неверным)";

    public IReadOnlyList<PlannedSet> Plan(GroupSpec g, BuildContext ctx)
    {
        if (g.Bottom is null && g.Top is null)
            throw new JobException("NO_MAT", $"{g.Key}: generator=footing требует bottom и/или top");

        double cover = ctx.CoverFt(g);
        bool hooked = string.Equals(g.Edge, "hook", StringComparison.OrdinalIgnoreCase);
        (string? hookStart, string? hookEnd) = ctx.HooksOf(g);
        if (!hooked) (hookStart, hookEnd) = (null, null);

        double minBarLenFt = ctx.U.Ft(g.MinBarLength);
        Footprint? fp = g.UseFootprint ? Footprint.TryExtract(ctx.Host) : null;
        if (g.UseFootprint && fp is null) ctx.Notes.Add($"{g.Key}: {FallbackNote}");
        else if (fp is not null) ctx.Notes.Add($"{g.Key}: {fp.Describe()}");

        var plan = new List<PlannedSet>();

        if (g.Bottom is not null)
            AddMat(plan, g, ctx, fp, cover, g.Bottom, true, hookStart, hookEnd, minBarLenFt);
        if (g.Top is not null)
            AddMat(plan, g, ctx, fp, cover, g.Top, false, hookStart, hookEnd, minBarLenFt);

        if (plan.Count == 0)
            throw new JobException("EMPTY_MAT", $"{g.Key}: в bottom/top нет ни x, ни y");

        return plan;
    }

    private void AddMat(
        List<PlannedSet> plan, GroupSpec g, BuildContext ctx, Footprint? fp, double cover,
        MatSpec mat, bool fromBottom, string? hookStart, string? hookEnd, double minBarLenFt)
    {
        DirSpec? first = mat.X ?? mat.Y;
        if (first is null) return;
        bool firstIsX = mat.X is not null;
        DirSpec? second = mat.X is not null ? mat.Y : null;

        string firstBar = ctx.BarTypeOf(g, first.BarType);
        double dFirst = ctx.Types.DiameterFt(firstBar);

        // Диаметры настоящие: сетка из #8 поверх #4 иначе уедет в бетон или наружу.
        double face = fromBottom
            ? (fp?.BottomZ ?? ctx.Box.Min.Z) + cover
            : (fp?.TopZ ?? ctx.Box.Max.Z) - cover;
        double sign = fromBottom ? +1 : -1;
        string zone = fromBottom ? "bot" : "top";

        AddLayer(plan, g, ctx, fp, cover, first, firstBar,
            face + sign * (dFirst / 2.0), firstIsX, zone, hookStart, hookEnd, minBarLenFt);

        if (second is null) return;

        string secondBar = ctx.BarTypeOf(g, second.BarType);
        double dSecond = ctx.Types.DiameterFt(secondBar);
        AddLayer(plan, g, ctx, fp, cover, second, secondBar,
            face + sign * (dFirst + dSecond / 2.0), false, zone, hookStart, hookEnd, minBarLenFt);
    }

    private void AddLayer(
        List<PlannedSet> plan, GroupSpec g, BuildContext ctx, Footprint? fp, double cover,
        DirSpec spec, string barType, double z, bool alongX, string zone,
        string? hookStart, string? hookEnd, double minBarLenFt)
    {
        double spacing = ctx.U.Ft(spec.Spacing);
        string axis = alongX ? "x" : "y";

        if (fp is not null)
        {
            AddByFootprint(plan, g, fp, cover, spacing, barType, z, alongX, zone, axis,
                hookStart, hookEnd, minBarLenFt, ctx.Notes);
            return;
        }

        AddByBoundingBox(plan, g, ctx, cover, spacing, barType, z, alongX, zone, axis,
            hookStart, hookEnd);
    }

    // ------------------------------------------------- раскладка по контуру

    private static void AddByFootprint(
        List<PlannedSet> plan, GroupSpec g, Footprint fp, double cover, double spacing,
        string barType, double z, bool alongX, string zone, string axis,
        string? hookStart, string? hookEnd, double minBarLenFt, List<string> notes)
    {
        // Угол задаёт направление стержней «x»; «y» перпендикулярно ему.
        double rad = g.Angle * Math.PI / 180.0 + (alongX ? 0 : Math.PI / 2);
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        var dir = new Pt2(cos, sin);

        List<LocalRail> rails =
            FieldLayout.LocalRails(fp.Outer, fp.Holes, dir, spacing, cover, cover);
        List<Band> all = FieldLayout.Bands(rails, spacing);

        // Отсев огрызков. У изрезанного контура сканлайн даёт в углах и вокруг
        // отверстий полосы в доли дюйма. Revit стержень короче дюйма считает
        // ОШИБКОЙ (не предупреждением), то есть проглотить её нельзя — она
        // поднимает модальный диалог и останавливает весь пакет.
        double minLen = Math.Max(LayoutMath.MinBarLengthFt, minBarLenFt);
        var bands = all.Where(b => b.Length >= minLen).ToList();
        int dropped = all.Count - bands.Count;
        if (dropped > 0)
            notes.Add($"{g.Key}: слой {zone}.{axis} — отброшено {dropped} полос(ы) короче "
                      + $"{minLen * 12:0.##} in из {all.Count}");

        if (bands.Count == 0)
            throw new JobException("EMPTY_LAYER",
                $"{g.Key}: слой {zone}.{axis} пуст — шаг {spacing:0.###} ft не помещается в контур "
                + $"с защитным слоем {cover:0.###} ft"
                + (dropped > 0 ? $" (все {dropped} полос(ы) короче минимума)" : string.Empty));

        for (int i = 0; i < bands.Count; i++)
        {
            Band b = bands[i];
            Vec3 a = Footprint.ToWorld(b.Start, b.Perp0, cos, sin, z);
            Vec3 c = Footprint.ToWorld(b.End, b.Perp0, cos, sin, z);

            // Раскладка набора — перпендикулярно стержню, в плоскости подошвы.
            var distribution = new Vec3(-sin, cos, 0);

            plan.Add(new PlannedSet(
                Polyline: [a, c],
                BarType: barType,
                Count: b.Count,
                SpacingFt: b.Spacing,
                Distribution: distribution,
                HookStart: hookStart,
                HookEnd: hookEnd,
                // Каждая полоса — свой набор, значит и свой ключ.
                Suffix: bands.Count == 1 ? $"{zone}{axis}" : $"{zone}{axis}-{i}"));
        }
    }

    // ------------------------------------------- запасная раскладка по габариту

    private static void AddByBoundingBox(
        List<PlannedSet> plan, GroupSpec g, BuildContext ctx, double cover, double spacing,
        string barType, double z, bool alongX, string zone, string axis,
        string? hookStart, string? hookEnd)
    {
        BoundingBoxXYZ box = ctx.Box;
        double spanX = box.Max.X - box.Min.X - 2 * cover;
        double spanY = box.Max.Y - box.Min.Y - 2 * cover;
        if (spanX <= 0 || spanY <= 0)
            throw new JobException("COVER_TOO_BIG",
                $"{g.Key}: защитный слой {cover:0.###} ft не оставляет места внутри подошвы");

        double barFrom = alongX ? box.Min.X + cover : box.Min.Y + cover;
        double barTo = alongX ? box.Max.X - cover : box.Max.Y - cover;
        double distMin = alongX ? box.Min.Y : box.Min.X;
        double distMax = alongX ? box.Max.Y : box.Max.X;

        (int count, double step, double first) =
            LayoutMath.WithinCover(distMin, distMax, cover, spacing);
        if (count <= 0)
            throw new JobException("EMPTY_LAYER",
                $"{g.Key}: слой {zone}.{axis} не поместился при шаге {spacing:0.###} ft");

        Vec3 a = alongX ? new Vec3(barFrom, first, z) : new Vec3(first, barFrom, z);
        Vec3 b = alongX ? new Vec3(barTo, first, z) : new Vec3(first, barTo, z);

        plan.Add(new PlannedSet(
            Polyline: [a, b],
            BarType: barType,
            Count: count,
            SpacingFt: step,
            Distribution: alongX ? Vec3.BasisY : Vec3.BasisX,
            HookStart: hookStart,
            HookEnd: hookEnd,
            Suffix: $"{zone}{axis}"));
    }
}
