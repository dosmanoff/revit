using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallReinforcement.Domain;
using WallReinforcement.Engine;

namespace WallReinforcement.Export;

/// <summary>Assembles a <see cref="WallDump"/> from a set of walls in the document.</summary>
public sealed class WallDumpBuilder
{
    private readonly Document _doc;

    /// <summary>Openings at least this wide AND tall (feet) get trim bars (matches the 12" slab rule).</summary>
    private const double TrimThresholdFt = 1.0;
    /// <summary>Walls at least this thick (inches) are candidates for transverse ties.</summary>
    private const double TieThicknessIn = 10.0;

    public WallDumpBuilder(Document doc) => _doc = doc;

    public WallDump Build(IReadOnlyList<Wall> walls, string generatedAtIso)
    {
        var dump = new WallDump
        {
            Document = new DocumentInfo { Title = _doc.Title, Path = NullIfEmpty(_doc.PathName) },
            GeneratedAt = generatedAtIso,
            Levels = CollectLevels(),
            AvailableRebarBarTypes = CollectBarTypes(),
            AvailableRebarHookTypes = CollectHookTypes(),
        };

        foreach (Wall wall in walls)
        {
            try
            {
                WallInfo info = BuildWall(wall, dump.WallTypesInUse);
                dump.Walls.Add(info);
            }
            catch (Exception ex)
            {
                dump.Warnings.Add($"Wall {wall.Id.Value}: {ex.Message}");
            }
        }

        return dump;
    }

    // ── Per-wall ──────────────────────────────────────────────────────────────────

    private WallInfo BuildWall(Wall wall, Dictionary<string, WallTypeInfo> typeReg)
    {
        WallAxes axes = WallAxes.For(wall);
        IReadOnlyList<OpeningRect> openings = WallGeometry.GetOpenings(axes);
        IReadOnlyList<WallJunction> junctions = WallJunctions.Detect(axes);

        double baseZ = axes.Origin.Z;

        var info = new WallInfo
        {
            ElementId = wall.Id.Value,
            Mark = StringParam(wall, BuiltInParameter.ALL_MODEL_MARK),
            Comments = StringParam(wall, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS),
            TypeId = wall.GetTypeId().Value,
            ThicknessIn = UnitConv.FtToIn(axes.Thickness),
            LengthFt = axes.Length,
            HeightFt = axes.Height,
            BaseElevationFt = baseZ,
            TopElevationFt = baseZ + axes.Height,
            StructuralUsage = StructuralUsageOf(wall),
            IsStructural = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() == 1,
            IsArc = (wall.Location as LocationCurve)?.Curve is Arc,
            Flipped = wall.Flipped,
            BaseLevel = LevelRefOf(wall.LevelId),
            TopLevel = LevelRefOf(wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId() ?? ElementId.InvalidElementId),
            RebarCover = ReadCover(wall),
            LocalBasis = BuildBasis(axes),
            Bbox = WorldBbox(wall),
        };

        RegisterType(wall, info, typeReg);
        info.Faces = BuildFaces(axes, openings);
        info.Openings = BuildOpenings(axes, openings);
        info.Junctions = BuildJunctions(axes, junctions);
        info.Hints = BuildHints(axes, info, junctions);

        return info;
    }

    private LocalBasisInfo BuildBasis(WallAxes axes) => new()
    {
        OriginFt = [axes.Origin.X, axes.Origin.Y, axes.Origin.Z],
        LengthDir = [axes.LengthDir.X, axes.LengthDir.Y],
        NormalDir = [axes.Normal.X, axes.Normal.Y],
        LengthWorldDeg = NormalizeDeg(Math.Atan2(axes.LengthDir.Y, axes.LengthDir.X) * 180.0 / Math.PI),
    };

    private static List<FaceInfo> BuildFaces(WallAxes axes, IReadOnlyList<OpeningRect> openings)
    {
        double gross = axes.Length * axes.Height;
        double holes = openings.Sum(o => o.Width * o.Height);
        double net = Math.Max(0, gross - holes);
        return
        [
            new FaceInfo { Side = "exterior", GrossAreaSf = gross, NetAreaSf = net },
            new FaceInfo { Side = "interior", GrossAreaSf = gross, NetAreaSf = net },
        ];
    }

    private List<OpeningInfo> BuildOpenings(WallAxes axes, IReadOnlyList<OpeningRect> openings)
    {
        var list = new List<OpeningInfo>(openings.Count);
        for (int i = 0; i < openings.Count; i++)
        {
            OpeningRect o = openings[i];
            Element? insert = _doc.GetElement(o.InsertId);
            var op = new OpeningInfo
            {
                Id = i + 1,
                InsertId = o.InsertId.Value,
                Category = insert?.Category?.Name,
                UMinFt = o.UMin, UMaxFt = o.UMax,
                VMinFt = o.VMin, VMaxFt = o.VMax,
                WidthFt = o.Width, HeightFt = o.Height,
                SillFt = o.VMin, HeadFt = o.VMax,
                NeedsTrim = o.Width >= TrimThresholdFt && o.Height >= TrimThresholdFt,
                Bbox = insert?.get_BoundingBox(null) is { } bb
                    ? new BboxInfo { MinFt = [bb.Min.X, bb.Min.Y, bb.Min.Z], MaxFt = [bb.Max.X, bb.Max.Y, bb.Max.Z] }
                    : new BboxInfo(),
            };
            if (insert is FamilyInstance fi && fi.Symbol is { } sym)
            {
                op.Family = sym.FamilyName;
                op.Type = sym.Name;
            }
            list.Add(op);
        }
        return list;
    }

    private List<JunctionInfo> BuildJunctions(WallAxes axes, IReadOnlyList<WallJunction> junctions)
    {
        var list = new List<JunctionInfo>(junctions.Count);
        foreach (WallJunction j in junctions)
        {
            list.Add(new JunctionInfo
            {
                Kind = j.Kind.ToString(),
                AtEnd = j.OurU < axes.Length * 0.5 ? "start" : "end",
                OurUFt = j.OurU,
                OtherWallId = j.OtherWall.Id.Value,
                OtherWallMark = StringParam(j.OtherWall, BuiltInParameter.ALL_MODEL_MARK),
                PointFt = [j.Point.X, j.Point.Y],
                OtherDir = [j.OtherDir.X, j.OtherDir.Y],
            });
        }
        return list;
    }

    private HintsInfo BuildHints(WallAxes axes, WallInfo info, IReadOnlyList<WallJunction> junctions)
    {
        var trim = info.Openings.Where(o => o.NeedsTrim).Select(o => o.Id).ToList();
        return new HintsInfo
        {
            RecommendedFaces = ["exterior", "interior"],
            NeedsOpeningTrim = trim.Count > 0,
            OpeningsNeedTrim = trim,
            HasCorners = junctions.Any(j => j.Kind == JunctionKind.LCorner),
            HasTJunctions = junctions.Any(j => j.Kind == JunctionKind.TStem),
            ThickEnoughForTies = info.ThicknessIn >= TieThicknessIn,
            RecommendedLayers = ["ExteriorVertical", "ExteriorHorizontal", "InteriorVertical", "InteriorHorizontal"],
        };
    }

    // ── Document-level collections ───────────────────────────────────────────────

    private List<LevelInfo> CollectLevels() =>
        new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => new LevelInfo { Name = l.Name, ElevationFt = l.Elevation, Id = l.Id.Value })
            .ToList();

    private List<BarTypeInfo> CollectBarTypes() =>
        new FilteredElementCollector(_doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .OrderBy(b => b.Name)
            .Select(b => new BarTypeInfo { Name = b.Name, NominalDiameterIn = UnitConv.FtToIn(b.BarNominalDiameter) })
            .ToList();

    private List<HookTypeInfo> CollectHookTypes() =>
        new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .OrderBy(h => h.Name)
            .Select(h => new HookTypeInfo { Name = h.Name })
            .ToList();

    private void RegisterType(Wall wall, WallInfo info, Dictionary<string, WallTypeInfo> reg)
    {
        if (_doc.GetElement(wall.GetTypeId()) is not WallType wt) return;

        info.Family = wt.FamilyName;
        info.Type = wt.Name;
        info.Function = FunctionOf(wt);

        string key = wt.Id.Value.ToString();
        if (reg.ContainsKey(key)) return;

        reg[key] = new WallTypeInfo
        {
            Family = wt.FamilyName,
            Type = wt.Name,
            Id = wt.Id.Value,
            ThicknessIn = info.ThicknessIn,
            Function = info.Function,
            StructuralMaterial = MaterialName(wt),
        };
    }

    // ── Small helpers ────────────────────────────────────────────────────────────

    private LevelRef? LevelRefOf(ElementId levelId)
    {
        if (levelId == ElementId.InvalidElementId) return null;
        return _doc.GetElement(levelId) is not Level lvl
            ? null
            : new LevelRef { Name = lvl.Name, Id = lvl.Id.Value, ElevationFt = lvl.Elevation };
    }

    private CoverInfo ReadCover(Wall wall) => new()
    {
        Exterior = ReadCoverFace(wall, BuiltInParameter.CLEAR_COVER_EXTERIOR),
        Interior = ReadCoverFace(wall, BuiltInParameter.CLEAR_COVER_INTERIOR),
        Other = ReadCoverFace(wall, BuiltInParameter.CLEAR_COVER_OTHER),
    };

    private CoverFace? ReadCoverFace(Wall wall, BuiltInParameter bip)
    {
        ElementId id = wall.get_Parameter(bip)?.AsElementId() ?? ElementId.InvalidElementId;
        if (id == ElementId.InvalidElementId) return null;
        if (_doc.GetElement(id) is not RebarCoverType cover) return null;
        return new CoverFace { Name = cover.Name, DistanceIn = UnitConv.FtToIn(cover.CoverDistance) };
    }

    private static string StructuralUsageOf(Wall wall)
    {
        Parameter? p = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM);
        return p is null ? "" : ((StructuralWallUsage)p.AsInteger()).ToString();
    }

    private static string FunctionOf(WallType wt)
    {
        Parameter? p = wt.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
        return p is null ? "" : ((WallFunction)p.AsInteger()).ToString();
    }

    private string? MaterialName(WallType wt)
    {
        ElementId id = wt.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)?.AsElementId()
                       ?? ElementId.InvalidElementId;
        return id != ElementId.InvalidElementId && _doc.GetElement(id) is Material m ? m.Name : null;
    }

    private static BboxInfo WorldBbox(Wall wall)
    {
        BoundingBoxXYZ? bb = wall.get_BoundingBox(null);
        return bb is null
            ? new BboxInfo()
            : new BboxInfo { MinFt = [bb.Min.X, bb.Min.Y, bb.Min.Z], MaxFt = [bb.Max.X, bb.Max.Y, bb.Max.Z] };
    }

    private static double NormalizeDeg(double deg)
    {
        deg %= 360.0;
        return deg < 0 ? deg + 360.0 : deg;
    }

    private static string? StringParam(Element e, BuiltInParameter bip)
    {
        string? v = e.get_Parameter(bip)?.AsString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
