using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Geometry;
using RevitPlugin.Domain.Placement;
using RevitPlugin.Domain.Reports;

namespace RevitPlugin.Domain.Rules;

/// <summary>
/// Краевая арматура по периметру стены: по 1–2 стержня вдоль каждой выбранной грани
/// (Top / Bottom — горизонтальные, Left / Right — вертикальные). Без L-загибов в углах
/// (MVP); загибы — в v1.0 через <c>l_leg_length</c>.
/// См. <c>docs/MODULES.md §M1.2</c>.
/// </summary>
public sealed class PerimeterEdgeRule : IRule
{
    public string Id => "wrs.perimeter_edge";

    public bool IsApplicable(WallContext wall, RebarConfig config)
    {
        var perim = config.Perimeter;
        return perim is { Enabled: true, EdgeRebar.Enabled: true }
               && wall.Length > 0 && wall.Height > 0 && wall.Thickness > 0;
    }

    public RuleResult Execute(WallContext wall, RebarConfig config)
    {
        var warnings = new List<string>();
        if (!IsApplicable(wall, config))
            return new RuleResult(Id, wall.Id, Array.Empty<RebarPlacement>(), warnings);

        var edgeCfg = config.Perimeter!.EdgeRebar;
        var edges = ParseEdges(edgeCfg.Edges, warnings);
        var facePositions = ResolveFacePositions(edgeCfg.Position, warnings);
        var count = Math.Max(1, Math.Min(2, edgeCfg.Count));

        var placements = new List<RebarPlacement>();

        foreach (var face in facePositions)
        {
            var faceOffset = ComputeFaceOffset(wall.Thickness, edgeCfg.EdgeCover, face);

            foreach (var edge in edges)
            {
                placements.AddRange(BuildBarsForEdge(wall, edgeCfg, edge, faceOffset, count));
            }
        }

        return new RuleResult(Id, wall.Id, placements, warnings);
    }

    private IEnumerable<RebarPlacement> BuildBarsForEdge(
        WallContext wall, PerimeterEdgeConfig cfg, PerimeterEdge edge, double faceOffset, int count)
    {
        // Локальные координаты: X — вдоль location line, Z — по высоте, Y — толщина (по нормали).
        var along = wall.LocationLine.Direction;
        var origin = wall.LocationLine.Start;
        var normal = wall.ExteriorNormal;

        // Двойник стержней (count=2) сдвигается на edge_cover * 2 ближе к центру грани.
        for (var i = 0; i < count; i++)
        {
            var innerShift = i == 0 ? 0 : cfg.EdgeCover; // вторая нитка глубже на edge_cover

            switch (edge)
            {
                case PerimeterEdge.Top:
                {
                    var z = wall.BaseElevation + wall.Height - cfg.EdgeCover - innerShift;
                    var start = origin + along * cfg.EndCover + normal * faceOffset + Vec3.UnitZ * z;
                    var end = origin + along * (wall.Length - cfg.EndCover) + normal * faceOffset + Vec3.UnitZ * z;
                    yield return RebarPlacement.Straight(Id, BarRole.Edge, cfg.BarType, start, end);
                    break;
                }
                case PerimeterEdge.Bottom:
                {
                    var z = wall.BaseElevation + cfg.EdgeCover + innerShift;
                    var start = origin + along * cfg.EndCover + normal * faceOffset + Vec3.UnitZ * z;
                    var end = origin + along * (wall.Length - cfg.EndCover) + normal * faceOffset + Vec3.UnitZ * z;
                    yield return RebarPlacement.Straight(Id, BarRole.Edge, cfg.BarType, start, end);
                    break;
                }
                case PerimeterEdge.Left:
                {
                    var x = cfg.EdgeCover + innerShift;
                    var basePoint = origin + along * x + normal * faceOffset;
                    var start = basePoint + Vec3.UnitZ * (wall.BaseElevation + cfg.EndCover);
                    var end = basePoint + Vec3.UnitZ * (wall.BaseElevation + wall.Height - cfg.EndCover);
                    yield return RebarPlacement.Straight(Id, BarRole.Edge, cfg.BarType, start, end);
                    break;
                }
                case PerimeterEdge.Right:
                {
                    var x = wall.Length - cfg.EdgeCover - innerShift;
                    var basePoint = origin + along * x + normal * faceOffset;
                    var start = basePoint + Vec3.UnitZ * (wall.BaseElevation + cfg.EndCover);
                    var end = basePoint + Vec3.UnitZ * (wall.BaseElevation + wall.Height - cfg.EndCover);
                    yield return RebarPlacement.Straight(Id, BarRole.Edge, cfg.BarType, start, end);
                    break;
                }
            }
        }
    }

    private static double ComputeFaceOffset(double thickness, double edgeCover, FacePosition face) => face switch
    {
        FacePosition.External => thickness / 2.0 - edgeCover,
        FacePosition.Internal => -(thickness / 2.0 - edgeCover),
        FacePosition.Center => 0,
        _ => 0
    };

    private static IReadOnlyList<PerimeterEdge> ParseEdges(string raw, List<string> warnings)
    {
        if (string.Equals(raw, "All", StringComparison.OrdinalIgnoreCase))
            return new[] { PerimeterEdge.Top, PerimeterEdge.Bottom, PerimeterEdge.Left, PerimeterEdge.Right };

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<PerimeterEdge>();
        foreach (var p in parts)
        {
            if (Enum.TryParse<PerimeterEdge>(p, ignoreCase: true, out var e))
                list.Add(e);
            else
                warnings.Add($"Unknown perimeter edge '{p}'; expected Top/Bottom/Left/Right/All.");
        }
        return list;
    }

    private static IReadOnlyList<FacePosition> ResolveFacePositions(string raw, List<string> warnings) => raw.ToLowerInvariant() switch
    {
        "external" => new[] { FacePosition.External },
        "internal" => new[] { FacePosition.Internal },
        "center" => new[] { FacePosition.Center },
        "both" => new[] { FacePosition.External, FacePosition.Internal },
        _ => Warn(warnings, $"Unknown position '{raw}'; falling back to Both.", new[] { FacePosition.External, FacePosition.Internal })
    };

    private static T Warn<T>(List<string> warnings, string message, T fallback)
    {
        warnings.Add(message);
        return fallback;
    }

    private enum PerimeterEdge { Top, Bottom, Left, Right }
    private enum FacePosition { External, Internal, Center }
}
