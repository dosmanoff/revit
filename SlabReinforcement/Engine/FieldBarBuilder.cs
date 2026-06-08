using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Engine;

/// <summary>
/// FieldMode=Bars: places the bottom (and, for TopMode=Continuous, top) mats as individual
/// bars — parallel rails clipped to the slab boundary minus openings, each long rail split at
/// the max bar length and lapped. The layout math is the unit-tested <see cref="FieldLayout"/>;
/// this class maps it onto the layer planes and creates tagged rebar.
/// </summary>
public sealed class FieldBarBuilder
{
    private readonly Document _doc;

    public FieldBarBuilder(Document doc) => _doc = doc;

    public int Build(SlabGeometry geom, SlabReinforcementConfig cfg, ElementId slabId)
    {
        IReadOnlyList<Loop2> holes = SlabOpenings.For(geom).Select(o => o.Boundary).ToList();

        double coverBottom = cfg.Ft(cfg.Cover.Bottom);
        double coverTop = cfg.Ft(cfg.Cover.Top);

        double bottomFace = geom.BottomElevationFt;
        double topFace = geom.TopElevationFt;

        int created = 0;

        // Bottom mat — X outermost (lowest), Y resting on it.
        double dbBx = Diameter(cfg.Field.BottomX.BarType);
        double dbBy = Diameter(cfg.Field.BottomY.BarType);
        created += BuildLayer(geom, cfg, slabId, SlabLayer.BottomX, geom.Basis.X, cfg.Field.BottomX,
            bottomFace + coverBottom + dbBx / 2, holes);
        created += BuildLayer(geom, cfg, slabId, SlabLayer.BottomY, geom.Basis.Y, cfg.Field.BottomY,
            bottomFace + coverBottom + dbBx + dbBy / 2, holes);

        // Top mat — only a full continuous mat here; OverSupports/Edges are placed by zones (PR-12).
        if (cfg.Field.TopMode == TopMode.Continuous)
        {
            double dbTx = Diameter(cfg.Field.TopX.BarType);
            double dbTy = Diameter(cfg.Field.TopY.BarType);
            created += BuildLayer(geom, cfg, slabId, SlabLayer.TopX, geom.Basis.X, cfg.Field.TopX,
                topFace - coverTop - dbTx / 2, holes);
            created += BuildLayer(geom, cfg, slabId, SlabLayer.TopY, geom.Basis.Y, cfg.Field.TopY,
                topFace - coverTop - dbTx - dbTy / 2, holes);
        }

        return created;
    }

    private int BuildLayer(
        SlabGeometry geom, SlabReinforcementConfig cfg, ElementId slabId,
        SlabLayer layer, Pt2 dir, LayerSpec spec, double planeZ, IReadOnlyList<Loop2> holes)
    {
        RebarBarType barType = RebarFactory.GetBarType(_doc, spec.BarType);   // strict — throws if missing
        double dbFt = barType.BarNominalDiameter;

        double spacing = cfg.Ft(spec.Spacing);
        double side = cfg.Ft(cfg.Cover.Side);
        double maxBar = cfg.Ft(cfg.Lengths.MaxBarLength);
        double lap = cfg.LapFeet(dbFt, spec.BarType, layer is SlabLayer.TopX or SlabLayer.TopY);

        List<Seg2> rails = FieldLayout.Rails(geom.Outer, holes, dir, spacing, side, side);
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, slabId, layer);

        int created = 0;
        for (int r = 0; r < rails.Count; r++)
        {
            Seg2 rail = rails[r];
            double len = rail.Length;
            if (len < 1e-6) continue;

            Pt2 unit = rail.Dir;
            double firstBar = cfg.Lengths.LapStagger && r % 2 == 1 ? maxBar / 2 : 0;

            foreach ((double s, double e) in FieldLayout.SplitWithLaps(len, maxBar, lap, firstBar))
            {
                XYZ a = new(rail.A.X + unit.X * s, rail.A.Y + unit.Y * s, planeZ);
                XYZ b = new(rail.A.X + unit.X * e, rail.A.Y + unit.Y * e, planeZ);
                RebarFactory.Create(_doc, RebarStyle.Standard, barType.Id, geom.Floor,
                    XYZ.BasisZ, new List<Curve> { Line.CreateBound(a, b) }, tag);
                created++;
            }
        }
        return created;
    }

    private double Diameter(string barTypeName)
    {
        try { return RebarFactory.GetBarType(_doc, barTypeName).BarNominalDiameter; }
        catch { return 0; }
    }
}
