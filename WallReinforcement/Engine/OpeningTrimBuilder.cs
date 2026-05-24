using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// Places straight trim bars and corner diagonals around each opening on a wall.
///
/// Layout (per face, repeated for exterior and interior):
///   - Top trim: horizontal bar from (uMin - ext, vMax) to (uMax + ext, vMax)
///   - Bottom trim: horizontal bar from (uMin - ext, vMin) to (uMax + ext, vMin)
///   - Left trim: vertical bar from (uMin, vMin - ext) to (uMin, vMax + ext)
///   - Right trim: vertical bar at uMax
///   - 4 diagonals, one at each opening corner, length L at the configured angle
/// </summary>
public class OpeningTrimBuilder
{
    private readonly Document _doc;

    public OpeningTrimBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, string tag)
    {
        OpeningsConfig op = cfg.Openings;
        if (!op.Enabled) return 0;

        double minWidthFt = UnitConv.MmToFt(op.MinWidthMm);

        ElementId trimBarTypeId = RebarFactory.LookupBarType(_doc, op.BarType);
        if (trimBarTypeId == ElementId.InvalidElementId) return 0;

        ElementId diagBarTypeId = op.Diagonals.Enabled
            ? RebarFactory.LookupBarType(_doc, op.Diagonals.BarType)
            : ElementId.InvalidElementId;

        IReadOnlyList<OpeningRect> openings = WallGeometry.GetOpenings(axes);
        int placed = 0;

        foreach (OpeningRect rect in openings)
        {
            if (rect.Width < minWidthFt) continue;

            placed += PlaceTrimsForOpening(axes, rect, cfg, trimBarTypeId, tag);

            if (op.Diagonals.Enabled && diagBarTypeId != ElementId.InvalidElementId)
                placed += PlaceDiagonalsForOpening(axes, rect, cfg, diagBarTypeId, tag);
        }

        return placed;
    }

    private int PlaceTrimsForOpening(WallAxes axes, OpeningRect rect, ReinforcementConfig cfg,
                                     ElementId barTypeId, string tag)
    {
        double ext = UnitConv.MmToFt(cfg.Openings.ExtensionMm);
        int n = 0;

        // Each face gets its own set of trims, offset from the wall centerplane.
        double extOffset =  axes.HalfThickness - UnitConv.MmToFt(cfg.Cover.ExteriorMm);
        double intOffset = -axes.HalfThickness + UnitConv.MmToFt(cfg.Cover.InteriorMm);

        foreach (double offset in new[] { extOffset, intOffset })
        {
            // Horizontals at top and bottom of opening.
            n += PlaceStraight(axes, barTypeId, tag,
                axes.At(rect.UMin - ext, rect.VMax, offset),
                axes.At(rect.UMax + ext, rect.VMax, offset));
            n += PlaceStraight(axes, barTypeId, tag,
                axes.At(rect.UMin - ext, rect.VMin, offset),
                axes.At(rect.UMax + ext, rect.VMin, offset));

            // Verticals at left and right of opening.
            n += PlaceStraight(axes, barTypeId, tag,
                axes.At(rect.UMin, rect.VMin - ext, offset),
                axes.At(rect.UMin, rect.VMax + ext, offset));
            n += PlaceStraight(axes, barTypeId, tag,
                axes.At(rect.UMax, rect.VMin - ext, offset),
                axes.At(rect.UMax, rect.VMax + ext, offset));
        }

        return n;
    }

    private int PlaceDiagonalsForOpening(WallAxes axes, OpeningRect rect, ReinforcementConfig cfg,
                                         ElementId barTypeId, string tag)
    {
        double len = UnitConv.MmToFt(cfg.Openings.Diagonals.LengthMm);
        double angRad = cfg.Openings.Diagonals.AngleDeg * Math.PI / 180.0;
        double du = len * Math.Cos(angRad);
        double dv = len * Math.Sin(angRad);
        int n = 0;

        double extOffset =  axes.HalfThickness - UnitConv.MmToFt(cfg.Cover.ExteriorMm);
        double intOffset = -axes.HalfThickness + UnitConv.MmToFt(cfg.Cover.InteriorMm);

        // Four corners: each diagonal points outward away from the opening.
        (double u, double v, double su, double sv)[] corners =
        {
            (rect.UMin, rect.VMin, -1, -1),  // bottom-left  → down-left
            (rect.UMax, rect.VMin, +1, -1),  // bottom-right → down-right
            (rect.UMin, rect.VMax, -1, +1),  // top-left     → up-left
            (rect.UMax, rect.VMax, +1, +1),  // top-right    → up-right
        };

        foreach (var (u, v, su, sv) in corners)
        foreach (double offset in new[] { extOffset, intOffset })
        {
            XYZ p1 = axes.At(u,           v,           offset);
            XYZ p2 = axes.At(u + su * du, v + sv * dv, offset);
            n += PlaceStraight(axes, barTypeId, tag, p1, p2);
        }

        return n;
    }

    private int PlaceStraight(WallAxes axes, ElementId barTypeId, string tag, XYZ p1, XYZ p2)
    {
        if (p1.DistanceTo(p2) < 1e-6) return 0;
        RebarFactory.Create(_doc, RebarStyle.Standard, barTypeId, axes.Wall,
                            axes.Normal, new List<Curve> { Line.CreateBound(p1, p2) }, tag);
        return 1;
    }
}
