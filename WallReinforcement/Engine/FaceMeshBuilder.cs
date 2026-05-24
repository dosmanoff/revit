using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;

namespace WallReinforcement.Engine;

/// <summary>
/// Places an <see cref="AreaReinforcement"/> on each side face of a wall and
/// sets its bar type / spacing per the <see cref="FaceConfig"/>.
/// Curves of the sketched area follow the wall outline inset by the cover values.
/// </summary>
public class FaceMeshBuilder
{
    private readonly Document _doc;

    public FaceMeshBuilder(Document doc) => _doc = doc;

    /// <summary>Place mesh on exterior + interior face per the provided config. Returns count created.</summary>
    public int Build(Wall wall, ReinforcementConfig cfg, string tag)
    {
        int created = 0;

        if (cfg.FaceMesh.Exterior is { } ext)
            created += BuildOne(wall, ShellLayerType.Exterior, ext, cfg, tag);

        if (cfg.FaceMesh.Interior is { } intr)
            created += BuildOne(wall, ShellLayerType.Interior, intr, cfg, tag);

        return created;
    }

    private int BuildOne(Wall wall, ShellLayerType side, FaceConfig face, ReinforcementConfig cfg, string tag)
    {
        IList<Reference> sideFaces = HostObjectUtils.GetSideFaces(wall, side);
        if (sideFaces.Count == 0) return 0;

        Reference faceRef = sideFaces[0];
        if (_doc.GetElement(faceRef).GetGeometryObjectFromReference(faceRef) is not Face geomFace)
            return 0;

        IList<Curve> boundary = BuildInsetBoundary(wall, geomFace, cfg.Cover, side);
        if (boundary.Count < 3) return 0;

        ElementId barTypeId   = RebarFactory.LookupBarType(_doc, face.Vertical.BarType);
        ElementId hookTypeId  = ElementId.InvalidElementId;
        ElementId areaTypeId  = DefaultAreaReinforcementTypeId();

        if (barTypeId == ElementId.InvalidElementId || areaTypeId == ElementId.InvalidElementId)
            return 0;

        // Major direction = vertical (along the wall's local Z, i.e. world up for a plumb wall).
        XYZ majorDir = XYZ.BasisZ;

        AreaReinforcement areaReinf = AreaReinforcement.Create(
            _doc, wall, boundary, majorDir, areaTypeId, barTypeId, hookTypeId);

        ApplyLayoutAndSpacing(areaReinf, face);
        ExistingRebarCleaner.Tag(areaReinf, tag);

        return 1;
    }

    /// <summary>
    /// Build a rectangular boundary on <paramref name="face"/>, inset from the wall outline
    /// by the cover values appropriate to <paramref name="side"/>.
    /// Phase-1 simplification: uses the face bounding rectangle in face UV space.
    /// </summary>
    private static IList<Curve> BuildInsetBoundary(Wall wall, Face face, CoverConfig cover, ShellLayerType side)
    {
        BoundingBoxUV bbUv = face.GetBoundingBox();
        double coverHoriz = UnitConv.MmToFt(cover.EndsMm);
        double coverBottom = UnitConv.MmToFt(cover.BottomMm);
        double coverTop    = UnitConv.MmToFt(cover.TopMm);

        double uMin = bbUv.Min.U + coverHoriz;
        double uMax = bbUv.Max.U - coverHoriz;
        double vMin = bbUv.Min.V + coverBottom;
        double vMax = bbUv.Max.V - coverTop;

        if (uMax <= uMin || vMax <= vMin) return [];

        XYZ p1 = face.Evaluate(new UV(uMin, vMin));
        XYZ p2 = face.Evaluate(new UV(uMax, vMin));
        XYZ p3 = face.Evaluate(new UV(uMax, vMax));
        XYZ p4 = face.Evaluate(new UV(uMin, vMax));

        return new List<Curve>
        {
            Line.CreateBound(p1, p2),
            Line.CreateBound(p2, p3),
            Line.CreateBound(p3, p4),
            Line.CreateBound(p4, p1),
        };
    }

    /// <summary>
    /// Configure the AreaReinforcement to lay bars at the spacings & bar types from
    /// <paramref name="face"/>. For walls we drive only the "front" layer (the one
    /// nearest the host face); the "back" layer stays inactive, and the orthogonal
    /// face gets its own AreaReinforcement on the opposite side.
    ///
    /// Direction conventions for an AreaReinforcement on a wall face:
    ///   DIR_1 = bars along the AreaReinforcement's major direction (passed at Create — vertical here)
    ///   DIR_2 = bars perpendicular to DIR_1 (horizontal here)
    /// </summary>
    private void ApplyLayoutAndSpacing(AreaReinforcement areaReinf, FaceConfig face)
    {
        TrySetInt(areaReinf, BuiltInParameter.REBAR_SYSTEM_LAYOUT_RULE, (int)RebarLayoutRule.MaximumSpacing);

        TrySetInt(areaReinf, BuiltInParameter.REBAR_SYSTEM_ACTIVE_FRONT_DIR_1, 1);
        TrySetInt(areaReinf, BuiltInParameter.REBAR_SYSTEM_ACTIVE_FRONT_DIR_2, 1);
        TrySetInt(areaReinf, BuiltInParameter.REBAR_SYSTEM_ACTIVE_BACK_DIR_1,  0);
        TrySetInt(areaReinf, BuiltInParameter.REBAR_SYSTEM_ACTIVE_BACK_DIR_2,  0);

        TrySetDouble(areaReinf, BuiltInParameter.REBAR_SYSTEM_SPACING_FRONT_DIR_1,
            UnitConv.MmToFt(face.Vertical.SpacingMm));
        TrySetDouble(areaReinf, BuiltInParameter.REBAR_SYSTEM_SPACING_FRONT_DIR_2,
            UnitConv.MmToFt(face.Horizontal.SpacingMm));

        ElementId vertBar  = RebarFactory.LookupBarType(_doc, face.Vertical.BarType);
        ElementId horizBar = RebarFactory.LookupBarType(_doc, face.Horizontal.BarType);
        TrySetElementId(areaReinf, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_FRONT_DIR_1, vertBar);
        TrySetElementId(areaReinf, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_FRONT_DIR_2, horizBar);
    }

    private ElementId DefaultAreaReinforcementTypeId()
    {
        var hit = new FilteredElementCollector(_doc)
            .OfClass(typeof(AreaReinforcementType))
            .FirstOrDefault();
        return hit?.Id ?? ElementId.InvalidElementId;
    }

    private static void TrySetInt(Element e, BuiltInParameter bip, int v)
    {
        Parameter? p = e.get_Parameter(bip);
        if (p is { IsReadOnly: false }) p.Set(v);
    }

    private static void TrySetDouble(Element e, BuiltInParameter bip, double v)
    {
        Parameter? p = e.get_Parameter(bip);
        if (p is { IsReadOnly: false }) p.Set(v);
    }

    private static void TrySetElementId(Element e, BuiltInParameter bip, ElementId id)
    {
        if (id == ElementId.InvalidElementId) return;
        Parameter? p = e.get_Parameter(bip);
        if (p is { IsReadOnly: false }) p.Set(id);
    }
}
