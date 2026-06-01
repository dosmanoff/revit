using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Engine;

/// <summary>
/// Places closing U-bars (П-образные) wrapping selected slab edges top-to-bottom: a hairpin
/// of two horizontal legs (near the top and bottom faces) joined by a vertical leg at the edge.
/// Targets free edges by default (<c>EdgeUBarSelector</c> = "free"), or "all" / explicit indices.
/// </summary>
public sealed class EdgeUBarBuilder
{
    private readonly Document _doc;

    public EdgeUBarBuilder(Document doc) => _doc = doc;

    public int Build(SlabGeometry geom, SlabReinforcementConfig cfg, ElementId slabId, SlabContext ctx)
    {
        RebarBarType barType = RebarFactory.GetBarType(_doc, cfg.Edges.BarType);   // strict
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, slabId, SlabLayer.EdgeU);

        double coverSide = cfg.Ft(cfg.Cover.Side);
        double coverTop = cfg.Ft(cfg.Cover.Top);
        double coverBottom = cfg.Ft(cfg.Cover.Bottom);
        double spacing = cfg.Ft(cfg.Edges.Spacing);
        double leg = cfg.Ft(cfg.Edges.Leg);

        double zTop = geom.TopElevationFt - coverTop;
        double zBottom = geom.BottomElevationFt + coverBottom;

        IReadOnlyList<int> targets = ResolveEdges(cfg.Edges.Selector, ctx);

        int created = 0;
        foreach (int idx in targets)
        {
            if (idx < 0 || idx >= ctx.Edges.Count) continue;
            BoundaryEdge edge = ctx.Edges[idx];
            created += BuildOnEdge(edge, geom, barType, tag, spacing, leg, coverSide, zTop, zBottom);
        }
        return created;
    }

    private int BuildOnEdge(
        BoundaryEdge edge, SlabGeometry geom, RebarBarType barType, string tag,
        double spacing, double leg, double coverSide, double zTop, double zBottom)
    {
        Seg2 seg = edge.Segment;
        double len = seg.Length;
        if (len <= 2 * coverSide) return 0;

        Pt2 t = seg.Dir;                                    // along the edge
        double rad = edge.OutwardNormalDeg * Math.PI / 180.0;
        var n = new Pt2(Math.Cos(rad), Math.Sin(rad));      // outward normal

        ElementId barTypeId = barType.Id;
        var normal = new XYZ(t.X, t.Y, 0);                  // U-bar bends in the vertical plane ⟂ edge

        int created = 0;
        foreach (double d in RebarFactory.EvenlySpaced(coverSide, len - coverSide, spacing))
        {
            Pt2 onEdge = seg.A + t * d;
            Pt2 atFace = onEdge - n * coverSide;            // pull the vertical leg inside the cover
            Pt2 inward = atFace - n * leg;

            var p1 = new XYZ(inward.X, inward.Y, zTop);
            var p2 = new XYZ(atFace.X, atFace.Y, zTop);
            var p3 = new XYZ(atFace.X, atFace.Y, zBottom);
            var p4 = new XYZ(inward.X, inward.Y, zBottom);

            var curves = new List<Curve>
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
            };
            RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, geom.Floor, normal, curves, tag);
            created++;
        }
        return created;
    }

    private static IReadOnlyList<int> ResolveEdges(string selector, SlabContext ctx)
    {
        string s = selector.Trim().ToLowerInvariant();
        if (s is "free" or "")
            return ctx.FreeEdgeIndices;
        if (s == "all")
            return Enumerable.Range(0, ctx.Edges.Count).ToList();

        var idx = new List<int>();
        foreach (string tok in s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(tok, out int v)) idx.Add(v);
        return idx;
    }
}
