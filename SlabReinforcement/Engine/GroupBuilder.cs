using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Geometry;
using UnitSystem = SlabReinforcement.Config.UnitSystem;

namespace SlabReinforcement.Engine;

/// <summary>
/// Places arbitrary additional-reinforcement groups from the JSON brief (#4): area bands
/// (support strips, bboxes, polygons) of parallel bars, and rows of dowels along a line or a
/// boundary segment that project out of the slab (into a wall/stair/slab above). Each group's
/// layer is written into the SR: tag. Heuristic first cut — refined with testing.
/// </summary>
public sealed class GroupBuilder
{
    private readonly Document _doc;

    public GroupBuilder(Document doc) => _doc = doc;

    public int Build(SlabGeometry geom, SlabContext ctx, ElementId slabId,
        SlabReinforcementConfig cfg, IReadOnlyList<BriefGroup> groups)
    {
        UnitSystem u = cfg.Units;
        int created = 0;
        foreach (BriefGroup g in groups)
        {
            try { created += BuildGroup(geom, ctx, slabId, cfg, g, u); }
            catch { /* one bad group must not sink the rest */ }
        }
        return created;
    }

    private int BuildGroup(SlabGeometry geom, SlabContext ctx, ElementId slabId,
        SlabReinforcementConfig cfg, BriefGroup g, UnitSystem u)
    {
        RebarBarType barType = RebarFactory.GetBarType(_doc, g.BarType);
        double db = barType.BarNominalDiameter;
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, slabId, ResolveLayer(g));
        double z = FaceZ(geom, cfg, g.Face, db);
        Pt2 dir = ResolveDir(g.Direction, geom, ctx);
        double spacing = g.Spacing is { } sp ? sp.ToFeet(u) : 1.0;

        return g.Region.Kind switch
        {
            RegionKind.Line or RegionKind.EdgeRange => BuildLine(geom, ctx, g, barType.Id, dir, spacing, z, tag, u),
            _ => BuildArea(geom, g, barType.Id, dir, spacing, z, tag, u),
        };
    }

    // ── Area bands (SupportStrip / BBox / Polygon): parallel bars clipped to the region ──

    private int BuildArea(SlabGeometry geom, BriefGroup g, ElementId barTypeId, Pt2 dir,
        double spacing, double z, string tag, UnitSystem u)
    {
        Loop2? region = RegionLoop(g.Region, geom, dir, u);
        if (region is null) return 0;

        double len = g.Length is { } l ? l.ToFeet(u) : 0;
        int created = 0;
        foreach (Seg2 rail in FieldLayout.Rails(region, [], dir, spacing, 0, 0))
        {
            Seg2 bar = len > 0 ? Recenter(rail, len) : rail;
            // Clip to the slab footprint so a strip centred on an edge support doesn't spill
            // past the slab edge or over a void ("rebar completely outside its host").
            foreach (Seg2 piece in FieldLayout.ClipToFootprint(bar, geom.Outer, geom.Openings))
            {
                RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, geom.Floor, XYZ.BasisZ,
                    new List<Curve> { Line.CreateBound(new XYZ(piece.A.X, piece.A.Y, z), new XYZ(piece.B.X, piece.B.Y, z)) }, tag);
                created++;
            }
        }
        return created;
    }

    // ── Line / EdgeRange: a row of bars or dowels along a line ────────────────────

    private int BuildLine(SlabGeometry geom, SlabContext ctx, BriefGroup g, ElementId barTypeId,
        Pt2 dir, double spacing, double z, string tag, UnitSystem u)
    {
        if (!LineEndpoints(g.Region, ctx, u, out Pt2 a, out Pt2 b)) return 0;

        double lineLen = (b - a).Length;
        if (lineLen < 1e-6) return 0;
        Pt2 t = (b - a).Normalized;

        IEnumerable<double> positions = g.Count is { } c && c > 0
            ? Enumerable.Range(0, c).Select(i => c == 1 ? lineLen / 2 : i * (lineLen / (c - 1)))
            : RebarFactory.EvenlySpaced(0, lineLen, spacing);

        int created = 0;
        foreach (double d in positions)
        {
            Pt2 p = a + t * d;
            if (g.Dowel is { } dw) created += Dowel(geom, barTypeId, tag, p, dw, u);
            else
            {
                double len = g.Length is { } l ? l.ToFeet(u) : 3.0;
                Pt2 q = p + dir * len;
                foreach (Seg2 piece in FieldLayout.ClipToFootprint(new Seg2(p, q), geom.Outer, geom.Openings))
                {
                    RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, geom.Floor, XYZ.BasisZ,
                        new List<Curve> { Line.CreateBound(new XYZ(piece.A.X, piece.A.Y, z), new XYZ(piece.B.X, piece.B.Y, z)) }, tag);
                    created++;
                }
            }
        }
        return created;
    }

    private int Dowel(SlabGeometry geom, ElementId barTypeId, string tag, Pt2 p, BriefDowel dw, UnitSystem u)
    {
        double embed = dw.EmbedLen.ToFeet(u);
        double project = dw.ProjectLen.ToFeet(u);
        bool up = !string.Equals(dw.Direction, "down", StringComparison.OrdinalIgnoreCase);

        double faceZ = up ? geom.TopElevationFt - 0.125 : geom.BottomElevationFt + 0.125;
        double endZ = up ? geom.TopElevationFt + project : geom.BottomElevationFt - project;

        var curves = new List<Curve>();
        var normal = XYZ.BasisX;
        if (string.Equals(dw.Bend, "90", StringComparison.Ordinal))
        {
            // horizontal embed leg in the slab + vertical projecting leg out
            double rad = dw.AngleDeg * Math.PI / 180.0;
            var legDir = new Pt2(Math.Cos(rad), Math.Sin(rad));
            Pt2 inward = p - legDir * embed;
            curves.Add(Line.CreateBound(new XYZ(inward.X, inward.Y, faceZ), new XYZ(p.X, p.Y, faceZ)));
            curves.Add(Line.CreateBound(new XYZ(p.X, p.Y, faceZ), new XYZ(p.X, p.Y, endZ)));
            normal = new XYZ(legDir.X, legDir.Y, 0);
        }
        else
        {
            double startZ = up ? geom.TopElevationFt - embed : geom.BottomElevationFt + embed;
            curves.Add(Line.CreateBound(new XYZ(p.X, p.Y, startZ), new XYZ(p.X, p.Y, endZ)));
        }

        RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, geom.Floor, normal, curves, tag);
        return 1;
    }

    // ── Region / direction helpers ────────────────────────────────────────────────

    private static Loop2? RegionLoop(BriefRegion r, SlabGeometry geom, Pt2 dir, UnitSystem u)
    {
        switch (r.Kind)
        {
            case RegionKind.BBox when r.BBox is { Length: 4 } c:
                return Rect(c[0], c[1], c[2], c[3]);

            case RegionKind.Polygon when r.Polygon is { Length: >= 6 } p:
                var pts = new List<Pt2>();
                for (int i = 0; i + 1 < p.Length; i += 2) pts.Add(new Pt2(p[i], p[i + 1]));
                return pts.Count >= 3 ? new Loop2(pts) : null;

            case RegionKind.SupportStrip when r.Support is { } mark:
                // band centred on the named support; width ⟂ dir, half-length = extent (else slab span)
                if (FindSupport(geom, mark) is not { } center) return null;
                Pt2 perp = dir.Perp;
                double halfW = (r.Width?.ToFeet(u) ?? 5.0) / 2;
                double ext = r.Extent?.ToFeet(u) ?? (geom.Bounds.Width + geom.Bounds.Height);
                Pt2 c1 = center - dir * ext - perp * halfW;
                Pt2 c2 = center + dir * ext - perp * halfW;
                Pt2 c3 = center + dir * ext + perp * halfW;
                Pt2 c4 = center - dir * ext + perp * halfW;
                return new Loop2([c1, c2, c3, c4]);

            default:
                return null;
        }
    }

    private static bool LineEndpoints(BriefRegion r, SlabContext ctx, UnitSystem u, out Pt2 a, out Pt2 b)
    {
        if (r.Kind == RegionKind.Line && r.LineFrom is { Length: 2 } f && r.LineTo is { Length: 2 } tt)
        {
            a = new Pt2(f[0], f[1]); b = new Pt2(tt[0], tt[1]); return true;
        }
        if (r.Kind == RegionKind.EdgeRange && r.Segment >= 0 && r.Segment < ctx.Edges.Count)
        {
            Seg2 seg = ctx.Edges[r.Segment].Segment;
            Pt2 t = seg.Dir;
            double from = r.From?.ToFeet(u) ?? 0;
            double to = r.To?.ToFeet(u) ?? seg.Length;
            a = seg.A + t * from; b = seg.A + t * Math.Min(to, seg.Length); return true;
        }
        a = default; b = default; return false;
    }

    private static Pt2? FindSupport(SlabGeometry geom, string mark)
    {
        SlabContext ctx = SlabContext.For(geom);
        SupportBelow? s = ctx.Supports.FirstOrDefault(x => string.Equals(x.Mark, mark, StringComparison.OrdinalIgnoreCase));
        return s?.CenterXY;
    }

    private static Pt2 ResolveDir(BriefDirection d, SlabGeometry geom, SlabContext ctx)
    {
        switch (d.Kind)
        {
            case DirectionKind.Axis:
                return string.Equals(d.Axis, "Y", StringComparison.OrdinalIgnoreCase) ? geom.Basis.Y : geom.Basis.X;
            case DirectionKind.World:
                double r = d.Deg * Math.PI / 180.0;
                return new Pt2(Math.Cos(r), Math.Sin(r));
            case DirectionKind.AlongEdge when d.Edge >= 0 && d.Edge < ctx.Edges.Count:
                return ctx.Edges[d.Edge].Segment.Dir;
            default:
                return geom.Basis.X;
        }
    }

    private static double FaceZ(SlabGeometry geom, SlabReinforcementConfig cfg, string face, double db) => face switch
    {
        "Bottom" => geom.BottomElevationFt + cfg.Ft(cfg.Cover.Bottom) + db / 2,
        "Mid" => (geom.TopElevationFt + geom.BottomElevationFt) / 2,
        _ => geom.TopElevationFt - cfg.Ft(cfg.Cover.Top) - db / 2,
    };

    private static Loop2 Rect(double x1, double y1, double x2, double y2)
    {
        double mnx = Math.Min(x1, x2), mxx = Math.Max(x1, x2), mny = Math.Min(y1, y2), mxy = Math.Max(y1, y2);
        return new Loop2([new(mnx, mny), new(mxx, mny), new(mxx, mxy), new(mnx, mxy)]);
    }

    private static Seg2 Recenter(Seg2 rail, double len)
    {
        Pt2 mid = rail.Mid;
        Pt2 t = rail.Dir;
        return new Seg2(mid - t * (len / 2), mid + t * (len / 2));
    }

    // Map a brief group's layer string to a tag layer. Explicit enum names (BottomX, TopY,
    // Support, Dowel, …) pass through; the friendly aliases "Bottom"/"Top" resolve to the
    // matching mesh layer by bar direction so Slab Views' Layer 1–4 pick the group up
    // (previously "Bottom"/"Top" fell through to Support — the additional-reinforcement layer bug).
    private static SlabLayer ResolveLayer(BriefGroup g)
    {
        string layer = (g.Layer ?? string.Empty).Trim();
        if (Enum.TryParse(layer, ignoreCase: true, out SlabLayer direct)) return direct;
        bool isY = string.Equals(g.Direction?.Axis, "Y", StringComparison.OrdinalIgnoreCase);
        return layer.ToLowerInvariant() switch
        {
            "bottom" => isY ? SlabLayer.BottomY : SlabLayer.BottomX,
            "top" => isY ? SlabLayer.TopY : SlabLayer.TopX,
            "dowel" => SlabLayer.Dowel,
            _ => SlabLayer.Support,
        };
    }
}
