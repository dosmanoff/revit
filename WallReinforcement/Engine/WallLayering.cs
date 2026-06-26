using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// Single source of truth for where each bar layer sits across the wall thickness, so every builder
/// (face bars, ties, edge U-bars, projections) lines up. Offsets are signed distances from the wall
/// centerplane along <see cref="WallAxes.Normal"/> (interior→exterior), in feet, usable directly as
/// the <c>offset</c> arg of <see cref="WallAxes.At"/>.
///
/// Layering convention (req 5): on each face the HORIZONTAL bars sit outboard at the cover line and
/// the VERTICAL bars nest one horizontal-bar-diameter inboard of them. When crossties are present
/// the tie wraps OUTSIDE both at the cover line, so the H and V layers nest one TIE diameter further
/// inboard — without this, Revit's commit-time rebar regeneration pulls the longitudinal bar at the
/// tie endpoint inside the tie and collapses the H layer onto the V layer (the "clash"). Diameters
/// come from <see cref="RebarBarType.BarNominalDiameter"/> (feet) — NOT the ACI "#n" table, which
/// would not match metric "Ø12" bar-type names.
/// </summary>
public class WallLayering
{
    private readonly double _t2, _cExt, _cInt;
    private readonly double _dbHExt, _dbVExt, _dbHInt, _dbVInt;
    private readonly double _tieInset;   // tie diameter when ties wrap this wall, else 0

    private WallLayering(double t2, double cExt, double cInt,
                         double dbHExt, double dbVExt, double dbHInt, double dbVInt, double tieInset)
    {
        _t2 = t2; _cExt = cExt; _cInt = cInt;
        _dbHExt = dbHExt; _dbVExt = dbVExt; _dbHInt = dbHInt; _dbVInt = dbVInt;
        _tieInset = tieInset;
    }

    public static WallLayering For(Document doc, WallAxes axes, ReinforcementConfig cfg)
    {
        double Db(string? barType) => barType is null ? 0 : NominalDiameterFeet(doc, barType);

        FaceConfig? ext = cfg.FaceMesh.Exterior;
        FaceConfig? intr = cfg.FaceMesh.Interior;

        // Ties wrap the cage only when enabled and the wall is thick enough (same gate as the tie
        // builder). When they do, the H/V layers must nest inside the tie.
        bool tiesActive = cfg.Ties.Enabled && axes.Thickness >= cfg.Ft(cfg.Ties.MinThickness);
        double tieInset = tiesActive ? Db(cfg.Ties.BarType) : 0;

        return new WallLayering(
            t2:   axes.HalfThickness,
            cExt: cfg.Ft(cfg.Cover.Exterior),
            cInt: cfg.Ft(cfg.Cover.Interior),
            dbHExt: Db(ext?.Horizontal.BarType),  dbVExt: Db(ext?.Vertical.BarType),
            dbHInt: Db(intr?.Horizontal.BarType), dbVInt: Db(intr?.Vertical.BarType),
            tieInset: tieInset);
    }

    /// <summary>Centerplane offset of the HORIZONTAL bar layer on the given face (just inside the tie,
    /// or at the cover line when there is no tie). Edge U-bars and corner/T continuity reference this.</summary>
    public double FieldFaceH(bool exterior) => exterior
        ?  _t2 - _cExt - _tieInset - _dbHExt / 2
        : -_t2 + _cInt + _tieInset + _dbHInt / 2;

    /// <summary>Centerplane offset of the VERTICAL bar layer — one horizontal-bar diameter inboard
    /// of <see cref="FieldFaceH"/>.</summary>
    public double FieldFaceV(bool exterior) => exterior
        ?  _t2 - _cExt - _tieInset - _dbHExt - _dbVExt / 2
        : -_t2 + _cInt + _tieInset + _dbHInt + _dbVInt / 2;

    /// <summary>Centerplane offset of the crosstie face (the tie sits at the cover line, wrapping the
    /// H and V layers). Equals the cover line when no tie diameter applies.</summary>
    public double TieFace(bool exterior) => exterior
        ?  _t2 - _cExt - _tieInset / 2
        : -_t2 + _cInt + _tieInset / 2;

    private static double NominalDiameterFeet(Document doc, string barType)
    {
        ElementId id = RebarFactory.LookupBarType(doc, barType);
        return id != ElementId.InvalidElementId && doc.GetElement(id) is RebarBarType t
            ? t.BarNominalDiameter
            : 0;
    }
}
