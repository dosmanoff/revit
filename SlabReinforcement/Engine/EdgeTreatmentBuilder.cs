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
            // curves define the bar CENTERLINE — keep the bar surface out of the cover
            double halfDb = barType.BarNominalDiameter / 2;

            foreach (int idx in ResolveSegments(edge.Segments, ctx))
            {
                if (idx < 0 || idx >= ctx.Edges.Count) continue;
                BoundaryEdge be = ctx.Edges[idx];
                created += Apply(geom, be, tr, barType.Id, spacing, leg, anchor,
                    coverSide + halfDb, zTop - halfDb, zBottom + halfDb, tag);
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

        // Bars spaced along the edge — placed as ONE rebar SET distributed along the edge (the set
        // normal is the edge direction), so the "show middle bar" presentation collapses them to a
        // single representative П. An edge too short for spaced bars gets one centred bar.
        List<double> ds = (len > 2 * coverSide
            ? RebarFactory.EvenlySpaced(coverSide, len - coverSide, spacing)
            : new[] { len / 2 }).ToList();
        if (ds.Count == 0) return 0;
        int count = ds.Count;
        double step = count > 1 ? ds[1] - ds[0] : 0;

        // first bar geometry; the set lays the rest out along +normal (= +t, the edge direction).
        Pt2 onEdge = seg.A + t * ds[0];
        Pt2 atFace = onEdge - n * coverSide;          // inside the side cover
        Pt2 inward = atFace - n * leg;

        switch (tr.Type)
        {
            case EdgeTreatmentType.UBar:
                return SetOf(barTypeId, geom.Floor, normal, tag, count, step, new List<Curve>
                {
                    Line.CreateBound(new XYZ(inward.X, inward.Y, zTop), new XYZ(atFace.X, atFace.Y, zTop)),
                    Line.CreateBound(new XYZ(atFace.X, atFace.Y, zTop), new XYZ(atFace.X, atFace.Y, zBottom)),
                    Line.CreateBound(new XYZ(atFace.X, atFace.Y, zBottom), new XYZ(inward.X, inward.Y, zBottom)),
                });

            case EdgeTreatmentType.Bend90:
                int b90 = 0;
                if (top) b90 += SetOf(barTypeId, geom.Floor, normal, tag, count, step, new List<Curve>
                {
                    Line.CreateBound(new XYZ(inward.X, inward.Y, zTop), new XYZ(atFace.X, atFace.Y, zTop)),
                    Line.CreateBound(new XYZ(atFace.X, atFace.Y, zTop), new XYZ(atFace.X, atFace.Y, zTop - leg)),
                });
                if (bottom) b90 += SetOf(barTypeId, geom.Floor, normal, tag, count, step, new List<Curve>
                {
                    Line.CreateBound(new XYZ(inward.X, inward.Y, zBottom), new XYZ(atFace.X, atFace.Y, zBottom)),
                    Line.CreateBound(new XYZ(atFace.X, atFace.Y, zBottom), new XYZ(atFace.X, atFace.Y, zBottom + leg)),
                });
                return b90;

            case EdgeTreatmentType.IntoSupport:
                Pt2 outPt = atFace + n * anchor;
                Pt2 inPt = atFace - n * Math.Max(leg, anchor);
                int isup = 0;
                if (top) isup += SetOf(barTypeId, geom.Floor, normal, tag, count, step, new List<Curve>
                { Line.CreateBound(new XYZ(inPt.X, inPt.Y, zTop), new XYZ(outPt.X, outPt.Y, zTop)) });
                if (bottom) isup += SetOf(barTypeId, geom.Floor, normal, tag, count, step, new List<Curve>
                { Line.CreateBound(new XYZ(inPt.X, inPt.Y, zBottom), new XYZ(outPt.X, outPt.Y, zBottom)) });
                return isup;

            case EdgeTreatmentType.Straight:
                // "Cover only": the field mat already runs to the edge, so no extra edge bar here.
                // For a continuous edge bar, use a `group` with an EdgeRange region instead.
                return 0;

            default:
                return 0;
        }
    }

    /// <summary>Create one rebar from <paramref name="curves"/> and, when more than one position,
    /// distribute it along the set normal as a number-with-spacing set. Returns the bar count.</summary>
    private int SetOf(ElementId barTypeId, Element host, XYZ normal, string tag, int count, double step, List<Curve> curves)
    {
        Rebar set = RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, host, normal, curves, tag);
        if (count > 1 && step > 1e-6)
            try { set.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(count, step, true, true, true); }
            catch { /* keep the single representative bar if the layout API rejects it */ }
        return count;
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
