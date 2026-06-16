using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;

namespace StairsReinforcement.Engine;

/// <summary>
/// Places starter / dowel bars where a flight bears on a structural support (slab / beam / wall /
/// foundation — not a landing, which is handled by the knee builder). Each dowel embeds into the
/// support and projects up the flight along the bottom layer to lap the flight bottom main bars.
/// <c>Straight</c> = one line (embed + projection); <c>L</c> = horizontal embed leg + projection leg.
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
            if (!f.RebarHostOk) continue;
            if (Matches(f.LowerSupport, s.Host)) created += BuildStarter(f, cfg, s, bt, db, atUpper: false, tag);
            if (Matches(f.UpperSupport, s.Host)) created += BuildStarter(f, cfg, s, bt, db, atUpper: true, tag);
        }
        return created;
    }

    private int BuildStarter(FlightComponent f, StairsReinforcementConfig cfg, StarterConfig s,
        RebarBarType bt, double db, bool atUpper, string tag)
    {
        double nBot = cfg.Ft(cfg.Cover.Bottom) + db / 2;
        double uEnd = atUpper ? f.SlopeLengthFt : 0;
        double sgnIn = atUpper ? -1 : 1;                 // toward the flight interior
        double embed = cfg.Ft(s.Embed);
        double proj = cfg.Ft(s.Projection);

        double coverSide = cfg.Ft(cfg.Cover.Side);
        double w0 = -f.WidthFt / 2 + coverSide + db / 2;
        double span = f.WidthFt - 2 * (coverSide + db / 2);
        if (span < 0) { w0 = 0; span = 0; }
        (int count, double spacing) = BuildUtil.ResolveSet(s.SpacingMode, s.Count, cfg.Ft(s.Spacing), span);

        var Wv = new XYZ(f.Frame.W.X, f.Frame.W.Y, 0);
        XYZ normalW = Wv.IsZeroLength() ? XYZ.BasisX : Wv.Normalize();

        XYZ junction = BuildUtil.XYZof(f.Frame.At(uEnd, w0, nBot));
        XYZ projEnd = BuildUtil.XYZof(f.Frame.At(uEnd + sgnIn * proj, w0, nBot));

        var runHoriz = new XYZ(f.Frame.U.X, f.Frame.U.Y, 0);
        runHoriz = runHoriz.IsZeroLength() ? XYZ.BasisX : runHoriz.Normalize();
        XYZ intoSupport = runHoriz * (-sgnIn);          // away from the flight interior, into the support

        List<Curve> curves = s.Form == StarterForm.L
            ? new()
            {
                Line.CreateBound(junction + intoSupport * embed, junction),
                Line.CreateBound(junction, projEnd),
            }
            : new()
            {
                Line.CreateBound(BuildUtil.XYZof(f.Frame.At(uEnd - sgnIn * embed, w0, nBot)), projEnd),
            };

        return RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, f.Host, normalW, curves, tag, count, spacing);
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
