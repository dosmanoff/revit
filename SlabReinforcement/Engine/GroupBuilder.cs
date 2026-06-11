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
        Pt2 dir = ResolveDir(g.Direction, geom, ctx);
        // X-direction bars share the field X plane, Y-direction the field Y plane — otherwise an
        // additional Y band would lie in (and clash with) the crossing field X layer.
        bool isY = Math.Abs(dir.Dot(geom.Basis.Y)) > Math.Abs(dir.Dot(geom.Basis.X));
        double z = FaceZ(geom, cfg, g.Face, db, isY);
        double spacing = g.Spacing is { } sp ? sp.ToFeet(u) : 1.0;

        return g.Region.Kind switch
        {
            RegionKind.Line or RegionKind.EdgeRange => BuildLine(geom, ctx, g, barType.Id, dir, spacing, z, tag, u),
            RegionKind.SupportStrip => BuildSupportStrip(geom, cfg, g, barType.Id, dir, spacing, z, tag, u),
            _ => BuildArea(geom, cfg, g, barType.Id, dir, spacing, z, tag, u),
        };
    }

    // ── SupportStrip: N bars × length L FITTED INSIDE the slab around the support ────
    //
    // The strip prefers to centre on the support but is clamped inside the concrete (one-sided
    // at an edge/corner column), so the full bar count and length from the plan tag survive —
    // previously the centred strip was clipped at the slab edge and lost bars and length.

    private int BuildSupportStrip(SlabGeometry geom, SlabReinforcementConfig cfg, BriefGroup g,
        ElementId barTypeId, Pt2 dir, double spacing, double z, string tag, UnitSystem u)
    {
        if (g.Region.Support is not { } mark || FindSupport(geom, mark) is not { } center) return 0;
        Pt2 perp = dir.Perp;
        double cover = cfg.Ft(cfg.Cover.Side);
        IReadOnlyList<Loop2> holes = geom.Openings;

        double L = g.Length?.ToFeet(u) ?? 2 * (g.Region.Extent?.ToFeet(u) ?? 4.0);
        int n = g.Count ?? Math.Max(1, (int)Math.Round((g.Region.Width?.ToFeet(u) ?? 4.0) / spacing) + 1);

        if (FieldLayout.ConcreteInterval(center, dir, geom.Outer, holes) is not { } dInt) return 0;
        if (FieldLayout.ConcreteInterval(center, perp, geom.Outer, holes) is not { } pInt) return 0;
        double dLo = dInt.Lo + cover, dHi = dInt.Hi - cover;
        double pLo = pInt.Lo + cover, pHi = pInt.Hi - cover;
        if (dHi <= dLo || pHi <= pLo) return 0;

        double span = Math.Min(L, dHi - dLo);
        double s0 = Math.Clamp(-L / 2, dLo, dHi - span);          // centred, pushed inside
        double band = (n - 1) * spacing;
        if (band > pHi - pLo) { n = Math.Max(1, (int)((pHi - pLo) / spacing) + 1); band = (n - 1) * spacing; }
        double p0 = Math.Clamp(-band / 2, pLo, pHi - band);

        // clip each bar against holes (edge fit is already guaranteed), then band into sets
        var rails = new List<LocalRail>();
        for (int k = 0; k < n; k++)
        {
            Pt2 a = center + dir * s0 + perp * (p0 + k * spacing);
            Pt2 b = center + dir * (s0 + span) + perp * (p0 + k * spacing);
            foreach (Seg2 piece in FieldLayout.ClipToFootprint(new Seg2(a, b), geom.Outer, holes))
            {
                if (FieldLayout.InsetClippedEnds(piece, geom.Outer, holes, cover) is not { } bar) continue;
                double sa = bar.A.Dot(dir), sb = bar.B.Dot(dir);
                rails.Add(new LocalRail(bar.A.Dot(perp), Math.Min(sa, sb), Math.Max(sa, sb)));
            }
        }

        double bendLen = g.EdgeBend?.ToFeet(u) ?? 0;
        bool topFace = !string.Equals(g.Face, "Bottom", StringComparison.OrdinalIgnoreCase);
        var norm = new XYZ(perp.X, perp.Y, 0);

        int created = 0;
        foreach (Band run in FieldLayout.Bands(rails, spacing))
        {
            Pt2 a = dir * run.Start + perp * run.Perp0;
            Pt2 b = dir * run.End + perp * run.Perp0;
            List<Curve> curves = BarWithEdgeBends(geom, a, b, z, bendLen, topFace, cover);
            Rebar set = RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, geom.Floor, norm, curves, tag);
            if (run.Count > 1)
                try { set.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(run.Count, spacing, true, true, true); }
                catch { /* keep the representative bar */ }
            created += run.Count;
        }
        return created;
    }

    /// <summary>A bar from <paramref name="a"/> to <paramref name="b"/> at <paramref name="z"/>;
    /// an end sitting at the slab edge (within side cover + tolerance of the outer boundary) gets
    /// a 90° leg of <paramref name="bendLen"/> bent into the slab (down for top, up for bottom).</summary>
    private static List<Curve> BarWithEdgeBends(
        SlabGeometry geom, Pt2 a, Pt2 b, double z, double bendLen, bool topFace, double cover)
    {
        var pa = new XYZ(a.X, a.Y, z);
        var pb = new XYZ(b.X, b.Y, z);
        if (bendLen <= 1e-6) return new List<Curve> { Line.CreateBound(pa, pb) };

        double zLeg = topFace ? z - bendLen : z + bendLen;
        double tol = cover + 0.06;
        bool bendA = FieldLayout.OnBoundary(a, geom.Outer, geom.Openings, tol);
        bool bendB = FieldLayout.OnBoundary(b, geom.Outer, geom.Openings, tol);

        var curves = new List<Curve>();
        if (bendA) curves.Add(Line.CreateBound(new XYZ(a.X, a.Y, zLeg), pa));
        curves.Add(Line.CreateBound(pa, pb));
        if (bendB) curves.Add(Line.CreateBound(pb, new XYZ(b.X, b.Y, zLeg)));
        return curves;
    }

    // ── Area bands (BBox / Polygon): parallel bars clipped to the region ──

    private int BuildArea(SlabGeometry geom, SlabReinforcementConfig cfg, BriefGroup g, ElementId barTypeId, Pt2 dir,
        double spacing, double z, string tag, UnitSystem u)
    {
        Loop2? region = RegionLoop(g.Region, geom, dir, u);
        if (region is null) return 0;

        double len = g.Length is { } l ? l.ToFeet(u) : 0;
        double cover = cfg.Ft(cfg.Cover.Side);
        Pt2 perp = dir.Perp;

        // Parallel bars across the region (recentred to an explicit length if given), each clipped
        // to the slab footprint with the side cover restored at clipped ends, then regrouped into
        // uniform bands. Each band is placed as one rebar SET (SetLayoutAsNumberWithSpacing).
        var clipped = new List<LocalRail>();
        foreach (Seg2 rail in FieldLayout.Rails(region, [], dir, spacing, 0, 0))
        {
            Seg2 bar = len > 0 ? Recenter(rail, len) : rail;
            foreach (Seg2 piece in FieldLayout.ClipToFootprint(bar, geom.Outer, geom.Openings))
            {
                if (FieldLayout.InsetClippedEnds(piece, geom.Outer, geom.Openings, cover) is not { } inset) continue;
                double ly = inset.A.Dot(perp);
                double s = inset.A.Dot(dir), e = inset.B.Dot(dir);
                if (s > e) (s, e) = (e, s);
                clipped.Add(new LocalRail(ly, s, e));
            }
        }

        var norm = new XYZ(perp.X, perp.Y, 0);
        int created = 0;
        foreach (Band band in FieldLayout.Bands(clipped, spacing))
        {
            Pt2 a = dir * band.Start + perp * band.Perp0;
            Pt2 b = dir * band.End + perp * band.Perp0;
            Rebar set = RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, geom.Floor, norm,
                new List<Curve> { Line.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z)) }, tag);
            if (band.Count > 1)
                try { set.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(band.Count, spacing, true, true, true); }
                catch { /* keep the representative bar if the layout API rejects it */ }
            created += band.Count;
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

    /// <summary>Bar-axis plane: X-direction bars share the field X layer (at the face), Y-direction
    /// the field Y layer (one field-bar diameter further in), so additional bands nest with the mat
    /// instead of lying inside the cover / on top of the crossing layer.</summary>
    private double FaceZ(SlabGeometry geom, SlabReinforcementConfig cfg, string face, double db, bool isY)
    {
        return face switch
        {
            "Bottom" => geom.BottomElevationFt + cfg.Ft(cfg.Cover.Bottom)
                        + (isY ? Dia(cfg.Field.BottomX.BarType) + db / 2 : db / 2),
            "Mid" => (geom.TopElevationFt + geom.BottomElevationFt) / 2,
            _ => geom.TopElevationFt - cfg.Ft(cfg.Cover.Top)
                 - (isY ? Dia(cfg.Field.TopX.BarType) + db / 2 : db / 2),
        };
    }

    private double Dia(string barTypeName)
    {
        try { return RebarFactory.GetBarType(_doc, barTypeName).BarNominalDiameter; }
        catch { return 0; }
    }

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
        // Additional reinforcement gets its OWN layer (AddBottom / AddTop) so Slab Views can show it
        // on a dedicated view, separate from the 4 field-mat plans.
        return layer.ToLowerInvariant() switch
        {
            "bottom" => SlabLayer.AddBottom,
            "top" => SlabLayer.AddTop,
            "dowel" => SlabLayer.Dowel,
            _ => SlabLayer.Support,
        };
    }
}
