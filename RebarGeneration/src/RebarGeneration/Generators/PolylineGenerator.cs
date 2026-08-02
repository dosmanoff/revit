using RebarGeneration.Contracts;
using RebarGeneration.Core;

namespace RebarGeneration.Generators;

/// <summary>
/// Универсальный генератор: «вот полилинии, вот шаг и количество — сделай наборы».
/// <para>
/// Это запасной путь плагина и одновременно причина, по которой каркас получается
/// действительно универсальным. Плагин не обязан понимать каждый тип хоста:
/// там, где предметного генератора нет (ростверк нестандартной формы, отдельная
/// деталь, разовая группа), геометрию считает агент, а плагин по-прежнему даёт
/// транзакции, наборы, ключи, идемпотентность и отчёт.
/// </para>
/// <para>
/// Одна полилиния = форма ОДНОГО стержня. Набор из <c>count</c> стержней
/// раскладывается с шагом <c>spacing</c> вдоль <c>distribution</c> (по умолчанию —
/// нормаль плоскости стержня).
/// </para>
/// </summary>
public sealed class PolylineGenerator : IRebarGenerator
{
    public string Name => "polyline";

    public IReadOnlyList<PlannedSet> Plan(GroupSpec g, BuildContext ctx)
    {
        if (g.Curves is null || g.Curves.Count == 0)
            throw new JobException("NO_CURVES", $"{g.Key}: generator=polyline требует curves");

        string barType = ctx.BarTypeOf(g);
        (string? hookStart, string? hookEnd) = ctx.HooksOf(g);

        Vec3? distribution = null;
        if (g.Distribution is { Length: 3 } d)
        {
            var v = new Vec3(d[0], d[1], d[2]);
            // Направление — не длина: нормализуем и не тащим сюда единицы задания.
            if (v.Length > 1e-9) distribution = v.Normalized();
        }

        int count = Math.Max(1, g.Count);
        double spacingFt = ctx.U.Ft(g.Spacing);

        var plan = new List<PlannedSet>(g.Curves.Count);
        for (int i = 0; i < g.Curves.Count; i++)
        {
            List<double[]> raw = g.Curves[i];
            var pts = new List<Vec3>(raw.Count);
            foreach (double[] p in raw) pts.Add(ctx.U.Pt(p));

            plan.Add(new PlannedSet(
                Polyline: pts,
                BarType: barType,
                Count: count,
                SpacingFt: spacingFt,
                Distribution: distribution,
                HookStart: hookStart,
                HookEnd: hookEnd,
                Suffix: g.Curves.Count > 1 ? $"c{i}" : null));
        }

        return plan;
    }
}
