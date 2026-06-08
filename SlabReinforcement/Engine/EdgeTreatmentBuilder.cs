using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Geometry;
using UnitSystem = SlabReinforcement.Config.UnitSystem;

namespace SlabReinforcement.Engine;

/// <summary>
/// Per-boundary-segment edge reinforcement from the JSON brief (#3): each <see cref="BriefEdge"/>
/// targets one or more boundary segments (free / all / indices) and states how that edge is
/// formed — closing U-bar, 90° bend, anchored into the support, or straight. Spaced along the
/// segment. Tagged SR:...:EdgeU.
/// </summary>
public sealed class EdgeTreatmentBuilder
{
    private readonly Document _doc;

    public EdgeTreatmentBuilder(Document doc) => _doc = doc;

    private const double MinEdgeFt = 0.5;   // ignore boundary slivers shorter than 6"

    public int Build(SlabGeometry geom, SlabContext ctx, ElementId slabId,
        SlabReinforcementConfig cfg, IReadOnlyList<BriefEdge> edges, UnitSystem units)
    {
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, slabId, SlabLayer.EdgeU);
        double coverSide = cfg.Ft(cfg.Cover.Side);
        double zTop = geom.TopElevationFt - cfg.Ft(cfg.Cover.Top);
        double zBottom = geom.BottomElevationFt + cfg.Ft(cfg.Cover.Bottom);

        int created = 0;
        foreach (BriefEdge edge in edges)
        {
            EdgeTreatment tr = edge.Treatment;
            if (tr.Type == EdgeTreatmentType.None) continue;

            RebarBarType barType = RebarFactory.GetBarType(_doc, tr.BarType);
            double spacing = tr.Spacing.ToFeet(units);
            double leg = tr.Leg.ToFeet(units);
            double anchor = tr.AnchorLen.ToFeet(units);

            foreach (int idx in ResolveSegments(edge.Segments, ctx))
            {
                if (idx < 0 || idx >= ctx.Edges.Count) continue;
                BoundaryEdge be = ctx.Edges[idx];
                created += Apply(geom, be, tr, barType.Id, spacing, leg, anchor, coverSide, zTop, zBottom, tag);
            }
        }
        return created;
    }

    private int Apply(
        SlabGeometry geom, BoundaryEdge be, EdgeTreatment tr, ElementId barTypeId,
        double spacing, double leg, double anchor, double coverSide, double zTop, double zBottom, string tag)
    {
        Seg2 seg = be.Segment;
        double len = seg.Length;
        if (len < MinEdgeFt) return 0;       // skip only true slivers

        Pt2 t = seg.Dir;
        double rad = be.OutwardNormalDeg * Math.PI / 180.0;
        var n = new Pt2(Math.Cos(rad), Math.Sin(rad));   // outward
        var normal = new XYZ(t.X, t.Y, 0);               // bend plane ⟂ edge
        bool top = tr.Face is "both" or "top";
        bool bottom = tr.Face is "both" or "bottom";

        int created = 0;
        // Seed bars at `spacing` within the end cover; an edge too short for even one such
        // position (a small balcony return / perimeter jog) still gets one centred bar so the
        // free edge is closed.
        IEnumerable<double> positions = len > 2 * coverSide
            ? RebarFactory.EvenlySpaced(coverSide, len - coverSide, spacing)
            : new[] { len / 2 };
        foreach (double d in positions)
        {
            Pt2 onEdge = seg.A + t * d;
            Pt2 atFace = onEdge - n * coverSide;          // inside the side cover
            Pt2 inward = atFace - n * leg;

            switch (tr.Type)
            {
                case EdgeTreatmentType.UBar:
                    created += Curve3(barTypeId, geom.Floor, normal, tag,
                        new XYZ(inward.X, inward.Y, zTop), new XYZ(atFace.X, atFace.Y, zTop),
                        new XYZ(atFace.X, atFace.Y, zBottom), new XYZ(inward.X, inward.Y, zBottom));
                    break;

                case EdgeTreatmentType.Bend90:
                    // L-bars: a horizontal leg in the slab + a 90° vertical leg at the edge.
                    if (top)
                        created += Curve2(barTypeId, geom.Floor, normal, tag,
                            new XYZ(inward.X, inward.Y, zTop), new XYZ(atFace.X, atFace.Y, zTop),
                            new XYZ(atFace.X, atFace.Y, zTop - leg));
                    if (bottom)
                        created += Curve2(barTypeId, geom.Floor, normal, tag,
                            new XYZ(inward.X, inward.Y, zBottom), new XYZ(atFace.X, atFace.Y, zBottom),
                            new XYZ(atFace.X, atFace.Y, zBottom + leg));
                    break;

                case EdgeTreatmentType.IntoSupport:
                    // straight bars crossing the edge into the support
                    Pt2 outPt = atFace + n * anchor;
                    Pt2 inPt = atFace - n * Math.Max(leg, anchor);
                    if (top) created += Straight(barTypeId, geom.Floor, tag, inPt, outPt, zTop);
                    if (bottom) created += Straight(barTypeId, geom.Floor, tag, inPt, outPt, zBottom);
                    break;

                case EdgeTreatmentType.Straight:
                    // "Cover only": the field mat already runs to the edge, so no extra edge bar
                    // is placed here. (Previously this spawned a short perpendicular stub per
                    // spacing position on each face — hundreds of junk bars.) For a continuous
                    // edge bar, use a `group` with an EdgeRange region instead.
                    break;
            }
        }
        return created;
    }

    private int Curve3(ElementId barTypeId, Element host, XYZ normal, string tag, XYZ p1, XYZ p2, XYZ p3, XYZ p4)
    {
        RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, host, normal,
            new List<Curve> { Line.CreateBound(p1, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p4) }, tag);
        return 1;
    }

    private int Curve2(ElementId barTypeId, Element host, XYZ normal, string tag, XYZ p1, XYZ p2, XYZ p3)
    {
        RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, host, normal,
            new List<Curve> { Line.CreateBound(p1, p2), Line.CreateBound(p2, p3) }, tag);
        return 1;
    }

    private int Straight(ElementId barTypeId, Element host, string tag, Pt2 a, Pt2 b, double z)
    {
        RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, host, XYZ.BasisZ,
            new List<Curve> { Line.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z)) }, tag);
        return 1;
    }

    private static IReadOnlyList<int> ResolveSegments(string selector, SlabContext ctx)
    {
        string s = selector.Trim().ToLowerInvariant();
        if (s is "free" or "") return ctx.FreeEdgeIndices;
        if (s == "all") return Enumerable.Range(0, ctx.Edges.Count).ToList();

        var idx = new List<int>();
        foreach (string tok in s.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(tok, out int v)) idx.Add(v);
        return idx;
    }
}
