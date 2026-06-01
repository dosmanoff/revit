using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Engine;

/// <summary>
/// Places top/bottom strengthening bars in zones from <c>slab-zones.csv</c>: a band over a
/// named support, an axis-aligned BBox, or a Polygon. Bars run in the zone's direction at its
/// spacing, reusing the tested <see cref="FieldLayout"/> rails (clipped to the slab minus
/// openings) and split at the max bar length. Tagged SR:...:Support.
/// </summary>
public sealed class SupportZoneBuilder
{
    private readonly Document _doc;

    public SupportZoneBuilder(Document doc) => _doc = doc;

    public int Build(SlabGeometry geom, SlabReinforcementConfig cfg, ElementId slabId,
        SlabContext ctx, IReadOnlyList<ZoneSpec> zones)
    {
        if (zones.Count == 0) return 0;

        IReadOnlyList<Loop2> holes = SlabOpenings.For(geom).Select(o => o.Boundary).ToList();
        double coverSide = cfg.Ft(cfg.Cover.Side);
        double maxBar = cfg.Ft(cfg.Lengths.MaxBarLength);
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, slabId, SlabLayer.Support);

        int created = 0;
        foreach (ZoneSpec zone in zones)
        {
            RebarBarType barType = RebarFactory.GetBarType(_doc, zone.BarType);   // strict
            double dbFt = barType.BarNominalDiameter;
            double spacing = cfg.Ft(zone.Spacing);
            double lap = cfg.Lengths.LapMode == LapMode.Factor ? cfg.Lengths.LapFactor * dbFt : cfg.Ft(cfg.Lengths.LapLength);

            Pt2 dir = zone.Axis == ZoneAxis.X ? geom.Basis.X : geom.Basis.Y;
            Pt2 perp = dir.Perp;
            double z = PlaneZ(zone.Face, geom, cfg, dbFt);

            Region region = ResolveRegion(zone, ctx, dir, perp, cfg);
            if (region.Skip) continue;

            foreach (Seg2 rail in FieldLayout.Rails(geom.Outer, holes, dir, spacing, coverSide, coverSide))
            {
                double midPerp = rail.Mid.Dot(perp);
                if (Math.Abs(midPerp - region.CenterPerp) > region.PerpHalf) continue;
                if (region.Contains is not null && !region.Contains(rail.Mid)) continue;

                Seg2? clipped = ClampDir(rail, dir, region.DirMin, region.DirMax);
                if (clipped is not { } seg) continue;

                created += PlaceSplit(seg, barType.Id, geom.Floor, z, maxBar, lap, tag);
            }
        }
        return created;
    }

    private int PlaceSplit(Seg2 seg, ElementId barTypeId, Element host, double z, double maxBar, double lap, string tag)
    {
        Pt2 unit = seg.Dir;
        int created = 0;
        foreach ((double s, double e) in FieldLayout.SplitWithLaps(seg.Length, maxBar, lap))
        {
            var a = new XYZ(seg.A.X + unit.X * s, seg.A.Y + unit.Y * s, z);
            var b = new XYZ(seg.A.X + unit.X * e, seg.A.Y + unit.Y * e, z);
            RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, host, XYZ.BasisZ,
                new List<Curve> { Line.CreateBound(a, b) }, tag);
            created++;
        }
        return created;
    }

    private static double PlaneZ(ZoneFace face, SlabGeometry geom, SlabReinforcementConfig cfg, double dbFt) =>
        face == ZoneFace.Top
            ? geom.TopElevationFt - cfg.Ft(cfg.Cover.Top) - dbFt / 2
            : geom.BottomElevationFt + cfg.Ft(cfg.Cover.Bottom) + dbFt / 2;

    private readonly struct Region
    {
        public double CenterPerp { get; init; }
        public double PerpHalf { get; init; }
        public double DirMin { get; init; }
        public double DirMax { get; init; }
        public Func<Pt2, bool>? Contains { get; init; }
        public bool Skip { get; init; }

        public static Region Skipped => new() { Skip = true };
    }

    private static Region ResolveRegion(ZoneSpec zone, SlabContext ctx, Pt2 dir, Pt2 perp, SlabReinforcementConfig cfg)
    {
        switch (zone.ShapeKind)
        {
            case ZoneShapeKind.SupportMark:
            {
                SupportBelow? sup = ctx.Supports.FirstOrDefault(s =>
                    string.Equals(s.Mark, zone.SupportMark, StringComparison.OrdinalIgnoreCase));
                if (sup is null) return Region.Skipped;

                double extent = cfg.Ft(zone.Extent);
                double dirCenter = sup.CenterXY.Dot(dir);
                return new Region
                {
                    CenterPerp = sup.CenterXY.Dot(perp),
                    PerpHalf = cfg.Ft(zone.StripWidth) / 2,
                    DirMin = extent > 1e-9 ? dirCenter - extent : double.NegativeInfinity,
                    DirMax = extent > 1e-9 ? dirCenter + extent : double.PositiveInfinity,
                };
            }
            case ZoneShapeKind.BBox:
            {
                double x1 = zone.Coords[0], y1 = zone.Coords[1], x2 = zone.Coords[2], y2 = zone.Coords[3];
                double minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2);
                double minY = Math.Min(y1, y2), maxY = Math.Max(y1, y2);
                return new Region
                {
                    CenterPerp = 0,
                    PerpHalf = double.PositiveInfinity,
                    DirMin = double.NegativeInfinity,
                    DirMax = double.PositiveInfinity,
                    Contains = p => p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY,
                };
            }
            case ZoneShapeKind.Polygon:
            {
                var poly = new List<Pt2>();
                for (int i = 0; i + 1 < zone.Coords.Length; i += 2) poly.Add(new Pt2(zone.Coords[i], zone.Coords[i + 1]));
                return new Region
                {
                    CenterPerp = 0,
                    PerpHalf = double.PositiveInfinity,
                    DirMin = double.NegativeInfinity,
                    DirMax = double.PositiveInfinity,
                    Contains = p => Geometry2D.PointInLoop(poly, p),
                };
            }
            default:
                return Region.Skipped;
        }
    }

    private static Seg2? ClampDir(Seg2 rail, Pt2 dir, double dirMin, double dirMax)
    {
        if (double.IsNegativeInfinity(dirMin) && double.IsPositiveInfinity(dirMax)) return rail;

        double dA = rail.A.Dot(dir);
        double dB = rail.B.Dot(dir);
        double len = dB - dA;
        if (Math.Abs(len) < 1e-9)
            return dA >= dirMin && dA <= dirMax ? rail : null;

        double lo = (dirMin - dA) / len;
        double hi = (dirMax - dA) / len;
        if (len < 0) (lo, hi) = (hi, lo);

        double s0 = Math.Max(0, lo);
        double s1 = Math.Min(1, hi);
        if (s1 - s0 <= 1e-9) return null;

        Pt2 a = rail.A + (rail.B - rail.A) * s0;
        Pt2 b = rail.A + (rail.B - rail.A) * s1;
        return new Seg2(a, b);
    }
}
