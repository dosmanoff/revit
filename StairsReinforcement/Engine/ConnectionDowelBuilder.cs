using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Engine;

/// <summary>
/// Per flight↔support junction, places the two bent "green/red" connection dowel sets that lap the
/// flight planes to the support reinforcement (validated against the 435E STR-01 hand etalon —
/// see the stairs-etalon-reinforcement-scheme note). The flight bars themselves stay straight; these
/// dowels carry the lap. Geometry depends on the support kind:
/// <list type="bullet">
/// <item><b>landing</b>: GREEN = landing bottom mesh → flight BOTTOM plane, leg = max(¼ span, 4');
/// RED = landing top mesh → flight BOTTOM plane, leg = short. Support legs lie on/under the mesh.</item>
/// <item><b>slab</b> (floor the flight arrives at): GREEN = slab top mesh → flight TOP plane, long;
/// RED = slab bottom mesh → flight MID plane, short.</item>
/// <item><b>foundation</b>: GREEN = flight TOP plane, RED = flight BOTTOM plane; the support leg turns
/// straight DOWN into the slab by the foundation embed (no top/bottom mesh).</item>
/// </list>
/// </summary>
public sealed class ConnectionDowelBuilder
{
    private readonly Document _doc;
    public ConnectionDowelBuilder(Document doc) => _doc = doc;

    public int Build(FlightComponent f, StairsReinforcementConfig cfg, ElementId stairId)
    {
        DowelConfig d = cfg.Connections.Dowels;
        if (!d.Enabled) return 0;

        RebarBarType bt = RebarFactory.GetBarType(_doc, d.BarType);
        double db = bt.BarNominalDiameter;
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.Dowel);

        int created = 0;
        if (f.LowerSupport is { } lo) created += AtSupport(f, cfg, d, bt, db, tag, lo, atLowEnd: true);
        if (f.UpperSupport is { } up) created += AtSupport(f, cfg, d, bt, db, tag, up, atLowEnd: false);
        return created;
    }

    private int AtSupport(FlightComponent f, StairsReinforcementConfig cfg, DowelConfig d,
        RebarBarType bt, double db, string tag, SupportInfo support, bool atLowEnd)
    {
        FlightFrame fr = f.Frame;
        double cover = cfg.Ft(cfg.Cover.Bottom), side = cfg.Ft(cfg.Cover.Side), mdb = db;
        double nBot = cover + db / 2;
        double nTop = Math.Max(nBot, f.WaistFt - cfg.Ft(cfg.Cover.Top) - db / 2);
        double nMid = (nBot + nTop) / 2;
        double w0 = -f.WidthFt / 2 + side + db / 2;
        double span = Math.Max(2, f.SlopeLengthFt);
        double greenLen = Math.Max(cfg.Ft(d.GreenFlightMin), span / 4);
        double shortLeg = cfg.Ft(d.ShortLeg);
        int sign = atLowEnd ? +1 : -1;                       // +U climbs into the flight from a low support
        (int count, double spacing) = BuildUtil.ResolveSet(d.SpacingMode, d.Count, cfg.Ft(d.Spacing),
            f.WidthFt - 2 * (side + db / 2));

        var uH = new XYZ(fr.U.X, fr.U.Y, 0);
        uH = uH.IsZeroLength() ? XYZ.BasisY : uH.Normalize();
        XYZ supDir = (atLowEnd ? -1.0 : 1.0) * uH;            // horizontal, away from the flight into the support
        var normalW = new XYZ(fr.W.X, fr.W.Y, fr.W.Z);
        normalW = normalW.IsZeroLength() ? XYZ.BasisX : normalW.Normalize();

        int Dowel(double nPlane, double zSupport, double flightLen, double downEmbed)
        {
            double bu = (zSupport - fr.Origin.Z - fr.N.Z * nPlane) / (Math.Abs(fr.U.Z) < 1e-6 ? 1e-6 : fr.U.Z);
            XYZ bend = BuildUtil.XYZof(fr.At(bu, w0, nPlane));
            XYZ supEnd = downEmbed > 0
                ? new XYZ(bend.X, bend.Y, bend.Z - downEmbed)                 // foundation: turn down
                : new XYZ(bend.X + supDir.X * shortLeg, bend.Y + supDir.Y * shortLeg, zSupport);
            XYZ flEnd = BuildUtil.XYZof(fr.At(bu + sign * flightLen, w0, nPlane));
            var curves = new List<Curve> { Line.CreateBound(supEnd, bend), Line.CreateBound(bend, flEnd) };
            return RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, f.Host, normalW, curves, tag, count, spacing);
        }

        // ONLY a real foundation gets the turn-DOWN detail (there is no slab to lap into below it).
        if (atLowEnd && support.Kind == "foundation")
        {
            double embed = cfg.Ft(d.FoundationEmbed);
            return Dowel(nTop, fr.At(0, w0, nTop).Z, greenLen, embed)
                 + Dowel(nBot, fr.At(0, w0, nBot).Z, shortLeg, embed);
        }

        // Real structural top/bottom from the support's horizontal faces — NOT the solid bbox, whose
        // Min.Z on a native landing dips into the run-junction wedge and would drop the green leg out
        // the landing soffit (the "вылетел из площадки" / no-cover failure seen in the section).
        (double sTop, double sBot) = RevitGeom.SlabExtents(_doc.GetElement(new ElementId(support.ElementId)));
        if (sTop <= sBot) return 0;
        double coverTop = cfg.Ft(cfg.Cover.Top);

        // An UPPER slab the flight arrives at (a floor): green from its TOP mesh into the flight TOP
        // plane, red from its BOTTOM mesh into the flight MID plane.
        if (!atLowEnd && support.Kind == "slab")
        {
            double meshTop = sTop - coverTop - db / 2, meshBot = sBot + cover + db / 2;
            return Dowel(nTop, meshTop, greenLen, 0)
                 + Dowel(nMid, meshBot, shortLeg, 0);
        }

        // A landing (above or below): both lap the flight BOTTOM plane — green sits on the landing bottom
        // mesh, red under its top mesh (each +1 mesh dia off the face cover), so both stay inside the slab.
        double mTop = sTop - coverTop - db / 2 - mdb, mBot = sBot + cover + db / 2 + mdb;
        return Dowel(nBot, mBot, greenLen, 0)
             + Dowel(nBot, mTop, shortLeg, 0);
    }
}
