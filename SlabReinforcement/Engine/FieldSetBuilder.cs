using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Engine;

/// <summary>
/// FieldMode=Sets: places the field mats as Revit rebar <b>sets</b> — one Rebar element per
/// uniform band (a run of equal-length parallel bars), distributed in-plane via
/// <see cref="RebarShapeDrivenAccessor.SetLayoutAsNumberWithSpacing"/>. A set displays as the
/// representative (middle) bar; long bands are split lengthwise into overlapping sets (the lap).
/// Bars are clipped to the slab minus openings by the unit-tested <see cref="FieldLayout"/>.
/// </summary>
public sealed class FieldSetBuilder
{
    private readonly Document _doc;

    public FieldSetBuilder(Document doc) => _doc = doc;

    public int Build(SlabGeometry geom, SlabReinforcementConfig cfg, ElementId slabId)
    {
        IReadOnlyList<Loop2> holes = SlabOpenings.For(geom).Select(o => o.Boundary).ToList();
        double coverBottom = cfg.Ft(cfg.Cover.Bottom);
        double coverTop = cfg.Ft(cfg.Cover.Top);
        double bottomFace = geom.BottomElevationFt;
        double topFace = geom.TopElevationFt;

        int bars = 0;

        double dbBx = Diameter(cfg.Field.BottomX.BarType);
        double dbBy = Diameter(cfg.Field.BottomY.BarType);
        bars += BuildLayer(geom, cfg, slabId, SlabLayer.BottomX, geom.Basis.X, cfg.Field.BottomX,
            bottomFace + coverBottom + dbBx / 2, holes);
        bars += BuildLayer(geom, cfg, slabId, SlabLayer.BottomY, geom.Basis.Y, cfg.Field.BottomY,
            bottomFace + coverBottom + dbBx + dbBy / 2, holes);

        if (cfg.Field.TopMode == TopMode.Continuous)
        {
            double dbTx = Diameter(cfg.Field.TopX.BarType);
            double dbTy = Diameter(cfg.Field.TopY.BarType);
            bars += BuildLayer(geom, cfg, slabId, SlabLayer.TopX, geom.Basis.X, cfg.Field.TopX,
                topFace - coverTop - dbTx / 2, holes);
            bars += BuildLayer(geom, cfg, slabId, SlabLayer.TopY, geom.Basis.Y, cfg.Field.TopY,
                topFace - coverTop - dbTx - dbTy / 2, holes);
        }

        return bars;
    }

    private int BuildLayer(
        SlabGeometry geom, SlabReinforcementConfig cfg, ElementId slabId,
        SlabLayer layer, Pt2 dir, LayerSpec spec, double planeZ, IReadOnlyList<Loop2> holes)
    {
        RebarBarType barType = RebarFactory.GetBarType(_doc, spec.BarType);
        double dbFt = barType.BarNominalDiameter;

        double spacing = cfg.Ft(spec.Spacing);
        double side = cfg.Ft(cfg.Cover.Side);
        double maxBar = cfg.Ft(cfg.Lengths.MaxBarLength);
        double lap = cfg.LapFeet(dbFt, spec.BarType, layer is SlabLayer.TopX or SlabLayer.TopY);

        List<LocalRail> rails = FieldLayout.LocalRails(geom.Outer, holes, dir, spacing, side, side);
        List<Band> bands = FieldLayout.Bands(rails, spacing);
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, slabId, layer);

        Pt2 perp = dir.Perp;                                   // in-plane set-distribution direction
        var norm = new XYZ(perp.X, perp.Y, 0);

        int bars = 0;
        foreach (Band band in bands)
        {
            foreach ((double s, double e) in FieldLayout.SplitWithLaps(band.Length, maxBar, lap))
            {
                double lx0 = band.Start + s;
                double lx1 = band.Start + e;
                XYZ a = LocalToWorld(dir, perp, lx0, band.Perp0, planeZ);
                XYZ b = LocalToWorld(dir, perp, lx1, band.Perp0, planeZ);

                Rebar set = RebarFactory.Create(_doc, RebarStyle.Standard, barType.Id, geom.Floor,
                    norm, new List<Curve> { Line.CreateBound(a, b) }, tag);

                if (band.Count > 1)
                    try { set.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(band.Count, spacing, true, true, true); }
                    catch { /* fall back to the single representative bar */ }

                bars += band.Count;
            }
        }
        return bars;
    }

    private static XYZ LocalToWorld(Pt2 dir, Pt2 perp, double lx, double ly, double z) =>
        new(lx * dir.X + ly * perp.X, lx * dir.Y + ly * perp.Y, z);

    private double Diameter(string barTypeName)
    {
        try { return RebarFactory.GetBarType(_doc, barTypeName).BarNominalDiameter; }
        catch { return 0; }
    }
}
