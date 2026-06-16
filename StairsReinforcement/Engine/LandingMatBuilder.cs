using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Engine;

/// <summary>
/// Reinforces a landing as a two-way mat (bottom + top X/Y), like a small slab. Bars run along the
/// landing's local X or Y axis, each set distributed across the other axis and split/lapped if long.
/// (AreaSystem mode falls back to discrete bars for now — native AreaReinforcement is a later niche.)
/// </summary>
public sealed class LandingMatBuilder
{
    private readonly Document _doc;
    public LandingMatBuilder(Document doc) => _doc = doc;

    public int Build(LandingComponent l, StairsReinforcementConfig cfg, ElementId stairId)
    {
        if (cfg.Landing.Mode == FieldMode.AreaSystem)
        {
            int areaN = TryAreaSystem(l, cfg, stairId);
            if (areaN > 0) return areaN;   // else fall through to discrete bars
        }

        double coverBot = cfg.Ft(cfg.Cover.Bottom);
        double coverTop = cfg.Ft(cfg.Cover.Top);
        double zBot = l.ElevationFt - l.ThicknessFt;
        double zTop = l.ElevationFt;

        double dbBotX = Dia(cfg.Landing.BottomX), dbBotY = Dia(cfg.Landing.BottomY);
        double dbTopX = Dia(cfg.Landing.TopX), dbTopY = Dia(cfg.Landing.TopY);

        int created = 0;
        Pt2 X = l.Basis.X, Y = l.Basis.Y;

        if (cfg.Landing.BottomX.Enabled)
            created += Mat(l, cfg, cfg.Landing.BottomX, X, Y, zBot + coverBot + dbBotX / 2,
                ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.LandingBottomX));
        if (cfg.Landing.BottomY.Enabled)
            created += Mat(l, cfg, cfg.Landing.BottomY, Y, X, zBot + coverBot + dbBotX + dbBotY / 2,
                ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.LandingBottomY));

        if (cfg.Landing.TopMode != TopMode.None)
        {
            if (cfg.Landing.TopX.Enabled)
                created += Mat(l, cfg, cfg.Landing.TopX, X, Y, zTop - coverTop - dbTopX / 2,
                    ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.LandingTopX));
            if (cfg.Landing.TopY.Enabled)
                created += Mat(l, cfg, cfg.Landing.TopY, Y, X, zTop - coverTop - dbTopX - dbTopY / 2,
                    ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.LandingTopY));
        }

        return created;
    }

    /// <summary>Bars run along <paramref name="runDir"/>, set distributed along <paramref name="distDir"/>.</summary>
    private int Mat(LandingComponent l, StairsReinforcementConfig cfg, BarSetSpec spec,
        Pt2 runDir, Pt2 distDir, double z, string tag)
    {
        if (l.Boundary.Count < 3) return 0;
        RebarBarType bt = RebarFactory.GetBarType(_doc, spec.BarType);
        double db = bt.BarNominalDiameter;
        double cs = cfg.Ft(cfg.Cover.Side) + db / 2;

        // Extents of the boundary in (run, dist) relative to the basis origin.
        double rMin = double.MaxValue, rMax = double.MinValue, dMin = double.MaxValue, dMax = double.MinValue;
        foreach (Pt2 p in l.Boundary)
        {
            Pt2 rel = p - l.Basis.Origin;
            double r = rel.Dot(runDir), d = rel.Dot(distDir);
            rMin = Math.Min(rMin, r); rMax = Math.Max(rMax, r);
            dMin = Math.Min(dMin, d); dMax = Math.Max(dMax, d);
        }

        double r0 = rMin + cs, r1 = rMax - cs;
        double d0 = dMin + cs, d1 = dMax - cs;
        if (r1 - r0 <= 1e-6 || d1 - d0 <= 1e-6) return 0;

        (int count, double spacing) = BuildUtil.ResolveSet(spec.SpacingMode, spec.Count, cfg.Ft(spec.Spacing), d1 - d0);

        var normal = new XYZ(distDir.X, distDir.Y, 0);
        normal = normal.IsZeroLength() ? XYZ.BasisY : normal.Normalize();

        double maxLen = cfg.Ft(cfg.Lengths.MaxBarLength);
        double lap = BuildUtil.LapFt(cfg, db);
        var segs = BarSplitter.Split(r1 - r0, maxLen, lap);

        int created = 0;
        foreach (BarSplitter.Segment seg in segs)
        {
            XYZ p0 = At(l, runDir, distDir, r0 + seg.Start, d0, z);
            XYZ p1 = At(l, runDir, distDir, r0 + seg.End, d0, z);
            var curves = new List<Curve> { Line.CreateBound(p0, p1) };
            created += RebarFactory.CreateSet(_doc, RebarStyle.Standard, bt.Id, l.Host, normal, curves, tag, count, spacing);
        }
        return created;
    }

    private static XYZ At(LandingComponent l, Pt2 runDir, Pt2 distDir, double r, double d, double z) => new(
        l.Basis.Origin.X + runDir.X * r + distDir.X * d,
        l.Basis.Origin.Y + runDir.Y * r + distDir.Y * d,
        z);

    private double Dia(BarSetSpec spec)
    {
        if (!spec.Enabled) return 0;
        try { return RebarFactory.GetBarType(_doc, spec.BarType).BarNominalDiameter; }
        catch { return 0; }
    }

    // ── native AreaReinforcement mode ─────────────────────────────────────────

    /// <summary>Place a native AreaReinforcement mat; returns 0 (caller falls back to bars) on any issue.</summary>
    private int TryAreaSystem(LandingComponent l, StairsReinforcementConfig cfg, ElementId stairId)
    {
        try
        {
            if (l.Boundary.Count < 3) return 0;
            ElementId areaTypeId = new FilteredElementCollector(_doc).OfClass(typeof(AreaReinforcementType)).FirstElementId();
            ElementId majorBar = RebarFactory.LookupBarType(_doc, cfg.Landing.BottomX.BarType);
            if (areaTypeId == ElementId.InvalidElementId || majorBar == ElementId.InvalidElementId) return 0;

            var boundary = new List<Curve>();
            for (int i = 0; i < l.Boundary.Count; i++)
            {
                Pt2 a = l.Boundary[i], b = l.Boundary[(i + 1) % l.Boundary.Count];
                if ((b - a).Length < 1e-6) continue;
                boundary.Add(Line.CreateBound(new XYZ(a.X, a.Y, l.ElevationFt), new XYZ(b.X, b.Y, l.ElevationFt)));
            }
            if (boundary.Count < 3) return 0;

            var majorDir = new XYZ(l.Basis.X.X, l.Basis.X.Y, 0);
            AreaReinforcement area = AreaReinforcement.Create(
                _doc, l.Host, boundary, majorDir, areaTypeId, majorBar, ElementId.InvalidElementId);

            ConfigureArea(area, cfg);
            ExistingRebarCleaner.Tag(area, ExistingRebarCleaner.MakeTag(cfg.Name, stairId, StairLayer.LandingBottomX));
            return 1;
        }
        catch { return 0; }
    }

    private void ConfigureArea(AreaReinforcement area, StairsReinforcementConfig cfg)
    {
        bool top = cfg.Landing.TopMode != TopMode.None;
        SetInt(area, BuiltInParameter.REBAR_SYSTEM_LAYOUT_RULE, (int)RebarLayoutRule.MaximumSpacing);

        SetInt(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_BOTTOM_DIR_1, cfg.Landing.BottomX.Enabled ? 1 : 0);
        SetInt(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_BOTTOM_DIR_2, cfg.Landing.BottomY.Enabled ? 1 : 0);
        SetInt(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_TOP_DIR_1, top && cfg.Landing.TopX.Enabled ? 1 : 0);
        SetInt(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_TOP_DIR_2, top && cfg.Landing.TopY.Enabled ? 1 : 0);

        SetDouble(area, BuiltInParameter.REBAR_SYSTEM_SPACING_BOTTOM_DIR_1, cfg.Ft(cfg.Landing.BottomX.Spacing));
        SetDouble(area, BuiltInParameter.REBAR_SYSTEM_SPACING_BOTTOM_DIR_2, cfg.Ft(cfg.Landing.BottomY.Spacing));
        SetDouble(area, BuiltInParameter.REBAR_SYSTEM_SPACING_TOP_DIR_1, cfg.Ft(cfg.Landing.TopX.Spacing));
        SetDouble(area, BuiltInParameter.REBAR_SYSTEM_SPACING_TOP_DIR_2, cfg.Ft(cfg.Landing.TopY.Spacing));

        SetElem(area, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_BOTTOM_DIR_1, RebarFactory.LookupBarType(_doc, cfg.Landing.BottomX.BarType));
        SetElem(area, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_BOTTOM_DIR_2, RebarFactory.LookupBarType(_doc, cfg.Landing.BottomY.BarType));
        SetElem(area, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_TOP_DIR_1, RebarFactory.LookupBarType(_doc, cfg.Landing.TopX.BarType));
        SetElem(area, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_TOP_DIR_2, RebarFactory.LookupBarType(_doc, cfg.Landing.TopY.BarType));
    }

    private static void SetInt(Element e, BuiltInParameter bip, int v)
    { try { e.get_Parameter(bip)?.Set(v); } catch { } }
    private static void SetDouble(Element e, BuiltInParameter bip, double v)
    { try { e.get_Parameter(bip)?.Set(v); } catch { } }
    private static void SetElem(Element e, BuiltInParameter bip, ElementId v)
    { try { if (v != ElementId.InvalidElementId) e.get_Parameter(bip)?.Set(v); } catch { } }
}
