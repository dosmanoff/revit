using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Engine;

/// <summary>
/// FieldMode=AreaSystem: places the field mat as a native Revit <see cref="AreaReinforcement"/>
/// over the slab outer boundary (bottom layers always, top layers when TopMode=Continuous).
/// Revit lays out the run, so per-bar length / lap is not controlled here (use FieldMode=Bars
/// for that). Mirrors WallReinforcement.FaceMeshBuilder. Tagged SR:...:BottomX on the system.
/// </summary>
public sealed class FieldMeshBuilder
{
    private readonly Document _doc;

    public FieldMeshBuilder(Document doc) => _doc = doc;

    public int Build(SlabGeometry geom, SlabReinforcementConfig cfg, ElementId slabId)
    {
        ElementId areaTypeId = DefaultAreaType();
        ElementId majorBar = RebarFactory.GetBarType(_doc, cfg.Field.BottomX.BarType).Id;   // strict
        if (areaTypeId == ElementId.InvalidElementId) return 0;

        IList<Curve> boundary = OuterCurves(geom);
        if (boundary.Count < 3) return 0;

        var majorDir = new XYZ(geom.Basis.X.X, geom.Basis.X.Y, 0).Normalize();

        AreaReinforcement area = AreaReinforcement.Create(
            _doc, geom.Floor, boundary, majorDir, areaTypeId, majorBar, ElementId.InvalidElementId);

        ConfigureLayers(area, cfg);
        ExistingRebarCleaner.Tag(area, ExistingRebarCleaner.MakeTag(cfg.Name, slabId, SlabLayer.BottomX));
        return 1;
    }

    private IList<Curve> OuterCurves(SlabGeometry geom)
    {
        IReadOnlyList<Pt2> pts = geom.Outer.Points;
        double z = geom.TopElevationFt;
        var curves = new List<Curve>(pts.Count);
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var a = new XYZ(pts[i].X, pts[i].Y, z);
            var b = new XYZ(pts[(i + 1) % n].X, pts[(i + 1) % n].Y, z);
            if (a.DistanceTo(b) > 1e-6) curves.Add(Line.CreateBound(a, b));
        }
        return curves;
    }

    private ElementId DefaultAreaType() =>
        new FilteredElementCollector(_doc).OfClass(typeof(AreaReinforcementType)).FirstElementId();

    private void ConfigureLayers(AreaReinforcement area, SlabReinforcementConfig cfg)
    {
        TrySetInt(area, BuiltInParameter.REBAR_SYSTEM_LAYOUT_RULE, (int)RebarLayoutRule.MaximumSpacing);

        bool top = cfg.Field.TopMode == TopMode.Continuous;

        TrySetInt(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_BOTTOM_DIR_1, 1);
        TrySetInt(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_BOTTOM_DIR_2, 1);
        TrySetInt(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_TOP_DIR_1, top ? 1 : 0);
        TrySetInt(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_TOP_DIR_2, top ? 1 : 0);

        TrySetDouble(area, BuiltInParameter.REBAR_SYSTEM_SPACING_BOTTOM_DIR_1, cfg.Ft(cfg.Field.BottomX.Spacing));
        TrySetDouble(area, BuiltInParameter.REBAR_SYSTEM_SPACING_BOTTOM_DIR_2, cfg.Ft(cfg.Field.BottomY.Spacing));
        TrySetElem(area, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_BOTTOM_DIR_1, RebarFactory.LookupBarType(_doc, cfg.Field.BottomX.BarType));
        TrySetElem(area, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_BOTTOM_DIR_2, RebarFactory.LookupBarType(_doc, cfg.Field.BottomY.BarType));

        if (top)
        {
            TrySetDouble(area, BuiltInParameter.REBAR_SYSTEM_SPACING_TOP_DIR_1, cfg.Ft(cfg.Field.TopX.Spacing));
            TrySetDouble(area, BuiltInParameter.REBAR_SYSTEM_SPACING_TOP_DIR_2, cfg.Ft(cfg.Field.TopY.Spacing));
            TrySetElem(area, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_TOP_DIR_1, RebarFactory.LookupBarType(_doc, cfg.Field.TopX.BarType));
            TrySetElem(area, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_TOP_DIR_2, RebarFactory.LookupBarType(_doc, cfg.Field.TopY.BarType));
        }
    }

    private static void TrySetInt(Element e, BuiltInParameter bip, int v) => e.get_Parameter(bip)?.Set(v);
    private static void TrySetDouble(Element e, BuiltInParameter bip, double v) => e.get_Parameter(bip)?.Set(v);

    private static void TrySetElem(Element e, BuiltInParameter bip, ElementId id)
    {
        if (id != ElementId.InvalidElementId) e.get_Parameter(bip)?.Set(id);
    }
}
