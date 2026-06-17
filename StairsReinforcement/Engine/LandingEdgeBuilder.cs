using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Engine;

/// <summary>
/// Places the "пэшки" — U-bar (shape 17) sets wrapping each WALL-supported edge of a landing into the
/// wall (validated on the 435E STR-01 etalon). Per such edge: a set marching along the edge, each bar a
/// vertical middle segment inside the wall (at the wall vertical-bar plane) with two horizontal legs
/// reaching into the landing at the top and bottom mesh levels, so the mat edge is tied to the wall.
/// An edge counts as wall-supported when a structural wall sits just outside it.
/// </summary>
public sealed class LandingEdgeBuilder
{
    private readonly Document _doc;
    public LandingEdgeBuilder(Document doc) => _doc = doc;

    public int Build(LandingComponent l, StairsReinforcementConfig cfg, ElementId stairId)
    {
        PashkaConfig p = cfg.Connections.Pashki;
        if (!p.Enabled || l.Boundary.Count < 3) return 0;

        RebarBarType bt = RebarFactory.GetBarType(_doc, p.BarType);
        double db = bt.BarNominalDiameter;
        double cover = cfg.Ft(cfg.Cover.Bottom);
        double leg = cfg.Ft(p.Leg), wallOff = cover + db / 2;
        double zTop = l.ElevationFt - cfg.Ft(cfg.Cover.Top) - db / 2;
        double zBot = l.ElevationFt - l.ThicknessFt + cover + db / 2;
        if (zTop - zBot < 1e-3) return 0;
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.Pashka);

        // landing centroid (for the outward edge direction)
        double cx = 0, cy = 0;
        foreach (Pt2 q in l.Boundary) { cx += q.X; cy += q.Y; }
        cx /= l.Boundary.Count; cy /= l.Boundary.Count;

        int created = 0;
        for (int i = 0; i < l.Boundary.Count; i++)
        {
            Pt2 a = l.Boundary[i], b = l.Boundary[(i + 1) % l.Boundary.Count];
            double ex = b.X - a.X, ey = b.Y - a.Y, elen = Math.Sqrt(ex * ex + ey * ey);
            if (elen < 1.0) continue;                                   // skip stubs
            double dx = ex / elen, dy = ey / elen;                      // edge direction
            double nx = -dy, ny = dx;                                   // edge normal
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
            if (nx * (mx - cx) + ny * (my - cy) < 0) { nx = -nx; ny = -ny; }   // outward

            if (!WallOutside(mx + nx * 0.4, my + ny * 0.4, l.ElevationFt - 0.1)) continue;

            (int count, double spacing) = BuildUtil.ResolveSet(p.SpacingMode, p.Count, cfg.Ft(p.Spacing), elen - 1.0);
            // representative U at 0.5' in from the edge start, marching along the edge
            double sx = a.X + dx * 0.5, sy = a.Y + dy * 0.5;
            double wx = sx + nx * wallOff, wy = sy + ny * wallOff;      // middle, inside the wall
            var midTop = new XYZ(wx, wy, zTop);
            var midBot = new XYZ(wx, wy, zBot);
            var legTop = new XYZ(wx - nx * leg, wy - ny * leg, zTop);   // into the landing
            var legBot = new XYZ(wx - nx * leg, wy - ny * leg, zBot);
            var curves = new List<Curve>
            {
                Line.CreateBound(legTop, midTop),
                Line.CreateBound(midTop, midBot),
                Line.CreateBound(midBot, legBot),
            };
            var normal = new XYZ(dx, dy, 0);                            // set marches along the edge
            created += RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, l.Host, normal, curves, tag, count, spacing);
        }
        return created;
    }

    /// <summary>True if a structural wall solid is near the given world point (just outside a landing edge).</summary>
    private bool WallOutside(double x, double y, double z)
    {
        var pt = new XYZ(x, y, z);
        var box = new Outline(new XYZ(x - 0.2, y - 0.2, z - 0.2), new XYZ(x + 0.2, y + 0.2, z + 0.2));
        var col = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(box));
        return col.Any();
    }
}
