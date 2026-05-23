using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Geometry;
using RevitPlugin.Domain.Placement;
using RevitPlugin.Domain.Reports;

namespace RevitPlugin.Domain.Rules;

/// <summary>
/// Общая реализация правила «прямоугольная сетка» — раскладывает вертикальные и горизонтальные
/// стержни по габаритам прямой стены. Без учёта проёмов (это попадёт в M1 follow-up через
/// взаимодействие с <c>OpeningEdgeRule</c>).
/// </summary>
public abstract class MeshRuleBase : IRule
{
    public abstract string Id { get; }

    /// <summary>Знак отступа от грани вдоль нормали стены: +1 для наружной, −1 для внутренней.</summary>
    protected abstract double FaceSign { get; }

    /// <summary>Из конфига вынуть нужный <see cref="MeshConfig"/> (external/internal).</summary>
    protected abstract MeshConfig? GetMesh(RebarConfig config);

    protected abstract BarRole VerticalRole { get; }
    protected abstract BarRole HorizontalRole { get; }

    public bool IsApplicable(WallContext wall, RebarConfig config)
    {
        var mesh = GetMesh(config);
        return mesh is { Enabled: true } && wall.Length > 0 && wall.Height > 0 && wall.Thickness > 0;
    }

    public RuleResult Execute(WallContext wall, RebarConfig config)
    {
        var warnings = new List<string>();
        if (!IsApplicable(wall, config))
            return new RuleResult(Id, wall.Id, Array.Empty<RebarPlacement>(), warnings);

        var mesh = GetMesh(config)!;

        if (mesh.SpacingVertical < 2 * 12 /* минимум 2× номинального диаметра */)
            warnings.Add($"spacing_vertical={mesh.SpacingVertical} мм слишком мал; должен быть не меньше 2× диаметра.");
        if (mesh.SpacingHorizontal < 2 * 10)
            warnings.Add($"spacing_horizontal={mesh.SpacingHorizontal} мм слишком мал; должен быть не меньше 2× диаметра.");

        var placements = new List<RebarPlacement>();
        placements.AddRange(BuildVerticals(wall, mesh, warnings));
        placements.AddRange(BuildHorizontals(wall, mesh, warnings));

        return new RuleResult(Id, wall.Id, placements, warnings);
    }

    private IEnumerable<RebarPlacement> BuildVerticals(WallContext wall, MeshConfig mesh, List<string> warnings)
    {
        var positions = ComputePositions(
            extent: wall.Length,
            offsetStart: mesh.WallEndOffsetDistance,
            offsetEnd: mesh.WallEndOffsetDistance,
            spacing: mesh.SpacingVertical,
            mode: mesh.WallEndOffsetMode,
            warnings: warnings,
            label: "vertical");

        var faceOffset = wall.Thickness / 2.0 * FaceSign - mesh.Cover * FaceSign;
        var yBottom = wall.BaseElevation + mesh.VerticalOffsetBottom;
        var yTop = wall.BaseElevation + wall.Height - mesh.VerticalOffsetTop;

        if (yTop <= yBottom)
        {
            warnings.Add($"vertical_offset_top + vertical_offset_bottom ≥ height ({wall.Height}); вертикальные стержни не построены.");
            yield break;
        }

        var along = wall.LocationLine.Direction;
        var origin = wall.LocationLine.Start;

        foreach (var x in positions)
        {
            var basePoint = origin + along * x + wall.ExteriorNormal * faceOffset;
            var start = new Vec3(basePoint.X, basePoint.Y, yBottom);
            var end = new Vec3(basePoint.X, basePoint.Y, yTop);
            yield return RebarPlacement.Straight(Id, VerticalRole, mesh.BarTypeVertical, start, end);
        }
    }

    private IEnumerable<RebarPlacement> BuildHorizontals(WallContext wall, MeshConfig mesh, List<string> warnings)
    {
        var positions = ComputePositions(
            extent: wall.Height,
            offsetStart: mesh.VerticalOffsetBottom,
            offsetEnd: mesh.VerticalOffsetTop,
            spacing: mesh.SpacingHorizontal,
            mode: mesh.WallEndOffsetMode,
            warnings: warnings,
            label: "horizontal");

        var faceOffset = wall.Thickness / 2.0 * FaceSign - mesh.Cover * FaceSign;
        var xStart = mesh.HorizontalOffsetStart;
        var xEnd = wall.Length - mesh.HorizontalOffsetEnd;

        if (xEnd <= xStart)
        {
            warnings.Add($"horizontal_offset_start + horizontal_offset_end ≥ length ({wall.Length}); горизонтальные стержни не построены.");
            yield break;
        }

        var along = wall.LocationLine.Direction;
        var origin = wall.LocationLine.Start;

        foreach (var y in positions)
        {
            var startPoint = origin + along * xStart + wall.ExteriorNormal * faceOffset;
            var endPoint = origin + along * xEnd + wall.ExteriorNormal * faceOffset;
            var z = wall.BaseElevation + y;
            yield return RebarPlacement.Straight(
                Id,
                HorizontalRole,
                mesh.BarTypeHorizontal,
                new Vec3(startPoint.X, startPoint.Y, z),
                new Vec3(endPoint.X, endPoint.Y, z));
        }
    }

    /// <summary>
    /// Позиции стержней по одной оси. Дефолтный режим <c>FromStartEnd</c> —
    /// равные отступы от обоих концов. Возвращает координаты в локальной системе оси.
    /// </summary>
    public static IReadOnlyList<double> ComputePositions(
        double extent,
        double offsetStart,
        double offsetEnd,
        double spacing,
        string mode,
        List<string> warnings,
        string label)
    {
        if (spacing <= 0)
        {
            warnings.Add($"{label}: spacing={spacing} некорректен; стержни не построены.");
            return Array.Empty<double>();
        }

        double zoneStart;
        double zoneEnd;

        switch (mode)
        {
            case "FromStart":
                zoneStart = offsetStart;
                zoneEnd = extent;
                break;
            case "FromEnd":
                zoneStart = 0;
                zoneEnd = extent - offsetEnd;
                break;
            case "Centered":
                var half = (offsetStart + offsetEnd) / 2.0;
                zoneStart = half;
                zoneEnd = extent - half;
                break;
            case "FromStartEnd":
            default:
                zoneStart = offsetStart;
                zoneEnd = extent - offsetEnd;
                break;
        }

        var zone = zoneEnd - zoneStart;
        if (zone < 0)
        {
            warnings.Add($"{label}: суммарный offset ≥ extent ({extent}); fallback на центральный стержень.");
            return new[] { extent / 2.0 };
        }

        if (zone < spacing - 1e-6)
            return new[] { (zoneStart + zoneEnd) / 2.0 };

        var count = (int)Math.Floor(zone / spacing + 1e-6) + 1;
        var positions = new double[count];
        for (var i = 0; i < count; i++)
            positions[i] = zoneStart + i * spacing;
        return positions;
    }
}
