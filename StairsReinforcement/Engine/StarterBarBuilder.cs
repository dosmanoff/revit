using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;

namespace StairsReinforcement.Engine;

/// <summary>
/// Where a flight bears on a structural slab/foundation (NOT a landing — that junction is carried by
/// the flight bottom bars bending into the landing), places bent starter dowels HOSTED ON THE SLAB:
/// embedded in the slab and projecting up the flight bottom layer to lap the flight bottom main bars.
/// The flight bar itself does not enter the separate slab element; the slab's starter laps with it.
/// </summary>
public sealed class StarterBarBuilder
{
    private readonly Document _doc;
    public StarterBarBuilder(Document doc) => _doc = doc;

    public int Build(StairAssembly asm, StairsReinforcementConfig cfg, ElementId stairId)
    {
        StarterConfig s = cfg.Connections.Starters;
        if (!s.Enabled || s.Host == StarterHost.None) return 0;

        RebarBarType bt = RebarFactory.GetBarType(_doc, s.BarType);
        double db = bt.BarNominalDiameter;
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.Starter);

        int created = 0;
        foreach (FlightComponent f in asm.Flights)
        {
            if (Matches(f.LowerSupport, s.Host)) created += BuildStarter(f, f.LowerSupport!, cfg, s, bt, db, atUpper: false, tag);
            if (Matches(f.UpperSupport, s.Host)) created += BuildStarter(f, f.UpperSupport!, cfg, s, bt, db, atUpper: true, tag);
        }
        return created;
    }

    private int BuildStarter(FlightComponent f, SupportInfo support, StairsReinforcementConfig cfg,
        StarterConfig s, RebarBarType bt, double db, bool atUpper, string tag)
    {
        Element? host = _doc.GetElement(new ElementId(support.ElementId));
        if (host is null || !RevitGeom.IsValidRebarHost(host)) return 0;

        double nBot = cfg.Ft(cfg.Cover.Bottom) + db / 2;
        double uEnd = atUpper ? BuildUtil.RunTopU(f, nBot) : 0;
        double sgnIn = atUpper ? -1 : 1;                  // up the flight, into the interior
        double proj = cfg.Ft(s.Projection);               // lap with the flight bottom main
        double embed = cfg.Ft(s.Embed);

        double coverSide = cfg.Ft(cfg.Cover.Side);
        double w0 = -f.WidthFt / 2 + coverSide + db / 2;
        double span = f.WidthFt - 2 * (coverSide + db / 2);
        if (span < 0) { w0 = 0; span = 0; }
        (int count, double spacing) = BuildUtil.ResolveSet(s.SpacingMode, s.Count, cfg.Ft(s.Spacing), span);

        var Wv = new XYZ(f.Frame.W.X, f.Frame.W.Y, 0);
        XYZ normalW = Wv.IsZeroLength() ? XYZ.BasisX : Wv.Normalize();

        XYZ junction = BuildUtil.XYZof(f.Frame.At(uEnd, w0, nBot));
        XYZ projEnd = BuildUtil.XYZof(f.Frame.At(uEnd + sgnIn * proj, w0, nBot));
        // Embed into the slab: down for a slab below, up (clamped to the slab top) for a slab above.
        double embedZ = atUpper ? Math.Min(junction.Z + embed, support.ElevationFt) : junction.Z - embed;
        XYZ embedEnd = new(junction.X, junction.Y, embedZ);

        List<Curve> curves = s.Form == StarterForm.L
            ? new() { Line.CreateBound(embedEnd, junction), Line.CreateBound(junction, projEnd) }
            : new() { Line.CreateBound(embedEnd, projEnd) };

        return RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, host, normalW, curves, tag, count, spacing);
    }

    private static bool Matches(SupportInfo? support, StarterHost host)
    {
        if (support is null) return false;
        return host switch
        {
            StarterHost.Auto => support.Kind is "slab" or "beam" or "wall" or "foundation",
            StarterHost.SlabBelow => support.Kind == "slab",
            StarterHost.Beam => support.Kind == "beam",
            StarterHost.Wall => support.Kind == "wall",
            StarterHost.Foundation => support.Kind == "foundation",
            _ => false,
        };
    }
}
