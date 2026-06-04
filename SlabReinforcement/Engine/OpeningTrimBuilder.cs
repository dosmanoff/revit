using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Engine;

/// <summary>
/// Reinforces slab openings: extra straight bars parallel to each opening edge (top and
/// bottom, extended past the corners), optional U-bars wrapping the edge, and optional
/// 45° diagonal bars across the corners for crack control. Heuristic offsets — a Phase-3
/// first cut, refined later. All bars tagged SR:...:OpeningTrim.
/// </summary>
public sealed class OpeningTrimBuilder
{
    private readonly Document _doc;

    private const double TrimSpacingFt = 0.25;   // 3" between successive parallel trim bars
    private const double CornerOffsetFt = 0.25;  // diagonal bar offset into the slab from the corner

    public OpeningTrimBuilder(Document doc) => _doc = doc;

    public int Build(SlabGeometry geom, SlabReinforcementConfig cfg, ElementId slabId)
    {
        OpeningsConfig oc = cfg.Openings;
        RebarBarType trimType = RebarFactory.GetBarType(_doc, oc.BarType);       // strict
        RebarBarType diagType = RebarFactory.GetBarType(_doc, oc.DiagBarType);
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, slabId, SlabLayer.OpeningTrim);

        double zTop = geom.TopElevationFt - cfg.Ft(cfg.Cover.Top);
        double zBottom = geom.BottomElevationFt + cfg.Ft(cfg.Cover.Bottom);
        double ext = cfg.Ft(cfg.Anchors.EdgeAnchorLen);
        double coverSide = cfg.Ft(cfg.Cover.Side);
        double leg = cfg.Ft(cfg.Edges.Leg);
        double uSpacing = cfg.Ft(cfg.Edges.Spacing);

        IReadOnlyList<SlabOpening> openings = SlabOpenings.For(geom);
        IReadOnlyList<int> targets = ResolveOpenings(oc.Selector, openings);

        int created = 0;
        foreach (int idx in targets)
        {
            if (idx < 0 || idx >= openings.Count) continue;
            IReadOnlyList<Pt2> pts = openings[idx].Boundary.Points;
            Pt2 center = Centroid(pts);

            created += BuildTrim(pts, center, geom, trimType.Id, tag, oc.ExtraEachSide, zTop, zBottom, ext);
            if (oc.UBars)
                created += BuildUBars(pts, center, geom, trimType.Id, tag, zTop, zBottom, coverSide, leg, uSpacing);
            if (oc.Diagonals)
                created += BuildDiagonals(pts, center, geom, diagType.Id, tag, zTop, zBottom, ext);
        }
        return created;
    }

    private int BuildTrim(
        IReadOnlyList<Pt2> pts, Pt2 center, SlabGeometry geom, ElementId barTypeId, string tag,
        int extraEachSide, double zTop, double zBottom, double ext)
    {
        int created = 0;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            Pt2 a = pts[i], b = pts[(i + 1) % n];
            Pt2 t = (b - a).Normalized;
            Pt2 nrm = OutwardFromHole(t, (a + b) * 0.5, center);

            for (int k = 0; k < Math.Max(0, extraEachSide); k++)
            {
                Pt2 off = nrm * (TrimSpacingFt * (k + 1));
                Pt2 a2 = a + off - t * ext;
                Pt2 b2 = b + off + t * ext;
                created += Straight(geom, barTypeId, tag, a2, b2, zTop);
                created += Straight(geom, barTypeId, tag, a2, b2, zBottom);
            }
        }
        return created;
    }

    private int BuildUBars(
        IReadOnlyList<Pt2> pts, Pt2 center, SlabGeometry geom, ElementId barTypeId, string tag,
        double zTop, double zBottom, double coverSide, double leg, double uSpacing)
    {
        int created = 0;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            Pt2 a = pts[i], b = pts[(i + 1) % n];
            double len = (b - a).Length;
            if (len <= 2 * coverSide) continue;

            Pt2 t = (b - a).Normalized;
            Pt2 nrm = OutwardFromHole(t, (a + b) * 0.5, center);
            var normal = new XYZ(t.X, t.Y, 0);

            foreach (double d in RebarFactory.EvenlySpaced(coverSide, len - coverSide, uSpacing))
            {
                Pt2 atFace = a + t * d + nrm * coverSide;
                Pt2 inward = atFace + nrm * leg;
                var curves = new List<Curve>
                {
                    Line.CreateBound(new XYZ(inward.X, inward.Y, zTop), new XYZ(atFace.X, atFace.Y, zTop)),
                    Line.CreateBound(new XYZ(atFace.X, atFace.Y, zTop), new XYZ(atFace.X, atFace.Y, zBottom)),
                    Line.CreateBound(new XYZ(atFace.X, atFace.Y, zBottom), new XYZ(inward.X, inward.Y, zBottom)),
                };
                RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, geom.Floor, normal, curves, tag);
                created++;
            }
        }
        return created;
    }

    private int BuildDiagonals(
        IReadOnlyList<Pt2> pts, Pt2 center, SlabGeometry geom, ElementId barTypeId, string tag,
        double zTop, double zBottom, double ext)
    {
        int created = 0;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            Pt2 v = pts[i];
            Pt2 bisector = (v - center).Normalized;        // outward into the slab
            if (bisector.Length < 1e-6) continue;

            Pt2 mid = v + bisector * CornerOffsetFt;
            Pt2 dir = bisector.Perp;                        // bar crosses the diagonal crack
            Pt2 p0 = mid - dir * ext;
            Pt2 p1 = mid + dir * ext;

            created += Straight(geom, barTypeId, tag, p0, p1, zTop);
            created += Straight(geom, barTypeId, tag, p0, p1, zBottom);
        }
        return created;
    }

    private int Straight(SlabGeometry geom, ElementId barTypeId, string tag, Pt2 a, Pt2 b, double z)
    {
        var curve = Line.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z));
        RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, geom.Floor, XYZ.BasisZ, new List<Curve> { curve }, tag);
        return 1;
    }

    private static Pt2 OutwardFromHole(Pt2 edgeDir, Pt2 edgeMid, Pt2 center)
    {
        Pt2 cand = edgeDir.Perp;
        return (edgeMid - center).Dot(cand) < 0 ? cand * -1 : cand;
    }

    private static Pt2 Centroid(IReadOnlyList<Pt2> pts)
    {
        double sx = 0, sy = 0;
        foreach (Pt2 p in pts) { sx += p.X; sy += p.Y; }
        return new Pt2(sx / pts.Count, sy / pts.Count);
    }

    private static IReadOnlyList<int> ResolveOpenings(string selector, IReadOnlyList<SlabOpening> openings)
    {
        string s = selector.Trim().ToLowerInvariant();
        // "auto"/"trim" (default): only openings the classifier says need it (skips shafts and
        // openings hard against the slab edge / another big opening — the "excessive trim" fix).
        if (s is "auto" or "trim" or "")
            return Enumerable.Range(0, openings.Count).Where(i => openings[i].NeedsTrim).ToList();
        if (s == "none")
            return [];
        if (s == "all")
            return Enumerable.Range(0, openings.Count).ToList();

        var idx = new List<int>();
        foreach (string tok in s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(tok, out int v)) idx.Add(v - 1);     // ids are 1-based in the dump
        return idx;
    }
}
