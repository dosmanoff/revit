using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Geometry;
using RevitPlugin.Domain.Placement;
using RevitPlugin.Domain.Reports;

namespace RevitPlugin.Domain.Rules;

/// <summary>
/// Окантовка проёма прямыми стержнями по сторонам. Для каждого проёма стены —
/// от 0 до 4 стержней (top / bottom / left / right) на каждой выбранной грани
/// (external / internal / both), с анкеровкой за углы проёма.
/// MVP: без диагональных и U/O-хомутов — те приходят в M4. См. <c>docs/MODULES.md §M1.3</c>.
/// </summary>
public sealed class OpeningEdgeRule : IRule
{
    public string Id => "wrs.opening_edge";

    public bool IsApplicable(WallContext wall, RebarConfig config)
    {
        var opening = config.Opening;
        return opening is { Enabled: true, EdgeRebar.Enabled: true }
               && wall.Openings.Count > 0
               && wall.Length > 0 && wall.Height > 0 && wall.Thickness > 0;
    }

    public RuleResult Execute(WallContext wall, RebarConfig config)
    {
        var warnings = new List<string>();
        if (!IsApplicable(wall, config))
            return new RuleResult(Id, wall.Id, Array.Empty<RebarPlacement>(), warnings);

        var edgeCfg = config.Opening!.EdgeRebar;
        var sides = ParseSides(edgeCfg.Sides, warnings);
        var faces = ResolveFacePositions(edgeCfg.Position, warnings);
        var count = Math.Max(1, edgeCfg.Count);

        var placements = new List<RebarPlacement>();

        foreach (var opening in wall.Openings)
        {
            if (opening.Width < edgeCfg.MinWidth) continue;
            if (edgeCfg.MaxWidth is { } max && opening.Width > max) continue;

            foreach (var face in faces)
            {
                var faceOffset = ComputeFaceOffset(wall.Thickness, edgeCfg.EdgeCover, face);
                foreach (var side in sides)
                    placements.AddRange(BuildBarsForSide(wall, opening, edgeCfg, side, faceOffset, count));
            }
        }

        return new RuleResult(Id, wall.Id, placements, warnings);
    }

    private IEnumerable<RebarPlacement> BuildBarsForSide(
        WallContext wall, OpeningGeometry opening, OpeningEdgeConfig cfg,
        OpeningSide side, double faceOffset, int count)
    {
        var along = wall.LocationLine.Direction;
        var origin = wall.LocationLine.Start;
        var normal = wall.ExteriorNormal;

        for (var i = 0; i < count; i++)
        {
            var offsetIntoWall = cfg.EdgeCover * (i + 1);

            switch (side)
            {
                case OpeningSide.Top:
                {
                    var z = wall.BaseElevation + opening.YEnd + offsetIntoWall;
                    var x0 = Math.Max(0, opening.XStart - cfg.AnchorageLength);
                    var x1 = Math.Min(wall.Length, opening.XEnd + cfg.AnchorageLength);
                    var start = origin + along * x0 + normal * faceOffset + Vec3.UnitZ * z;
                    var end = origin + along * x1 + normal * faceOffset + Vec3.UnitZ * z;
                    yield return RebarPlacement.Straight(Id, BarRole.Opening, cfg.BarType, start, end);
                    break;
                }
                case OpeningSide.Bottom:
                {
                    var z = wall.BaseElevation + opening.YStart - offsetIntoWall;
                    if (z < wall.BaseElevation) continue; // не вылезаем за низ стены
                    var x0 = Math.Max(0, opening.XStart - cfg.AnchorageLength);
                    var x1 = Math.Min(wall.Length, opening.XEnd + cfg.AnchorageLength);
                    var start = origin + along * x0 + normal * faceOffset + Vec3.UnitZ * z;
                    var end = origin + along * x1 + normal * faceOffset + Vec3.UnitZ * z;
                    yield return RebarPlacement.Straight(Id, BarRole.Opening, cfg.BarType, start, end);
                    break;
                }
                case OpeningSide.Left:
                {
                    var x = opening.XStart - offsetIntoWall;
                    if (x < 0) continue;
                    var z0 = wall.BaseElevation + Math.Max(0, opening.YStart - cfg.AnchorageLength);
                    var z1 = wall.BaseElevation + Math.Min(wall.Height, opening.YEnd + cfg.AnchorageLength);
                    var basePoint = origin + along * x + normal * faceOffset;
                    var start = basePoint + Vec3.UnitZ * z0;
                    var end = basePoint + Vec3.UnitZ * z1;
                    yield return RebarPlacement.Straight(Id, BarRole.Opening, cfg.BarType, start, end);
                    break;
                }
                case OpeningSide.Right:
                {
                    var x = opening.XEnd + offsetIntoWall;
                    if (x > wall.Length) continue;
                    var z0 = wall.BaseElevation + Math.Max(0, opening.YStart - cfg.AnchorageLength);
                    var z1 = wall.BaseElevation + Math.Min(wall.Height, opening.YEnd + cfg.AnchorageLength);
                    var basePoint = origin + along * x + normal * faceOffset;
                    var start = basePoint + Vec3.UnitZ * z0;
                    var end = basePoint + Vec3.UnitZ * z1;
                    yield return RebarPlacement.Straight(Id, BarRole.Opening, cfg.BarType, start, end);
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

    private static IReadOnlyList<OpeningSide> ParseSides(string raw, List<string> warnings)
    {
        if (string.Equals(raw, "All", StringComparison.OrdinalIgnoreCase))
            return new[] { OpeningSide.Top, OpeningSide.Bottom, OpeningSide.Left, OpeningSide.Right };

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<OpeningSide>();
        foreach (var p in parts)
        {
            if (Enum.TryParse<OpeningSide>(p, ignoreCase: true, out var s))
                list.Add(s);
            else
                warnings.Add($"Unknown opening side '{p}'; expected Top/Bottom/Left/Right/All.");
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

    private enum OpeningSide { Top, Bottom, Left, Right }
    private enum FacePosition { External, Internal, Center }
}
