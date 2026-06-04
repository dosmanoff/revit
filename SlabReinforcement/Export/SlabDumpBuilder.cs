using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SlabReinforcement.Domain;
using SlabReinforcement.Engine;
using SlabReinforcement.Geometry;

namespace SlabReinforcement.Export;

/// <summary>Assembles a <see cref="SlabDump"/> from a set of floors in the document.</summary>
public sealed class SlabDumpBuilder
{
    private readonly Document _doc;

    public SlabDumpBuilder(Document doc) => _doc = doc;

    public SlabDump Build(IReadOnlyList<Floor> floors, string generatedAtIso)
    {
        var dump = new SlabDump
        {
            Document = new DocumentInfo { Title = _doc.Title, Path = NullIfEmpty(_doc.PathName) },
            GeneratedAt = generatedAtIso,
            Levels = CollectLevels(),
            AvailableRebarBarTypes = CollectBarTypes(),
            AvailableRebarHookTypes = CollectHookTypes(),
        };

        foreach (Floor floor in floors)
        {
            try
            {
                SlabInfo slab = BuildSlab(floor, dump.FloorTypesInUse);
                dump.Slabs.Add(slab);
            }
            catch (Exception ex)
            {
                dump.Warnings.Add($"Floor {floor.Id.Value}: {ex.Message}");
            }
        }

        return dump;
    }

    // ── Per-slab ────────────────────────────────────────────────────────────────

    private SlabInfo BuildSlab(Floor floor, Dictionary<string, FloorTypeInfo> typeReg)
    {
        SlabGeometry geom = SlabGeometry.For(floor);
        SlabContext ctx = SlabContext.For(geom);
        IReadOnlyList<SlabOpening> openings = SlabOpenings.For(geom);

        var slab = new SlabInfo
        {
            ElementId = floor.Id.Value,
            Mark = MarkOf(floor),
            Comments = StringParam(floor, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS),
            TypeId = floor.GetTypeId().Value,
            ThicknessIn = UnitConv.FtToIn(geom.ThicknessFt),
            TopElevationFt = geom.TopElevationFt,
            BottomElevationFt = geom.BottomElevationFt,
            IsStructural = floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.AsInteger() == 1,
            IsFoundation = floor.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation,
            AreaSf = geom.NetAreaSf,
            Level = LevelRefOf(floor),
            RebarCover = ReadCover(floor),
            LocalBasis = BuildBasis(geom),
            Bbox = BoundsToBbox(geom.Bounds),
        };

        RegisterType(floor, slab, typeReg);
        slab.Boundary = BuildBoundary(ctx);
        slab.Openings = BuildOpenings(openings);
        slab.Context = BuildContext(ctx);
        slab.Hints = BuildHints(geom, ctx, openings);

        return slab;
    }

    private static LocalBasisInfo BuildBasis(SlabGeometry geom) => new()
    {
        OriginFt = [geom.Bounds.MinX, geom.Bounds.MinY, geom.BottomElevationFt],
        XDir = [geom.Basis.X.X, geom.Basis.X.Y],
        YDir = [geom.Basis.Y.X, geom.Basis.Y.Y],
        XWorldDeg = geom.XWorldDeg,
    };

    private static List<BoundaryEdgeInfo> BuildBoundary(SlabContext ctx)
    {
        var list = new List<BoundaryEdgeInfo>(ctx.Edges.Count);
        foreach (BoundaryEdge e in ctx.Edges)
        {
            var info = new BoundaryEdgeInfo
            {
                Index = e.Index,
                StartFt = [e.Segment.A.X, e.Segment.A.Y],
                EndFt = [e.Segment.B.X, e.Segment.B.Y],
                LengthFt = e.LengthFt,
                MidNormalWorldDeg = e.OutwardNormalDeg,
                Edge = e.Kind.ToString().ToLowerInvariant(),
            };
            if (e.AdjacentElementId != 0)
                info.Adjacent = new AdjacentInfo { Kind = e.Kind.ToString(), ElementId = e.AdjacentElementId, Mark = e.AdjacentMark };
            list.Add(info);
        }
        return list;
    }

    private static List<OpeningInfo> BuildOpenings(IReadOnlyList<SlabOpening> openings)
    {
        var list = new List<OpeningInfo>(openings.Count);
        for (int i = 0; i < openings.Count; i++)
        {
            SlabOpening o = openings[i];
            list.Add(new OpeningInfo
            {
                Id = i + 1,
                Source = o.Source.ToString(),
                AreaSf = o.AreaSf,
                NeedsTrim = o.NeedsTrim,
                Bbox = BoundsToBbox(o.Bounds),
                Boundary = LoopToSegments(o.Boundary),
            });
        }
        return list;
    }

    private static ContextInfo BuildContext(SlabContext ctx)
    {
        var info = new ContextInfo
        {
            SupportsBelow = ctx.Supports.Select(s => new SupportInfo
            {
                Kind = s.Kind.ToString(),
                ElementId = s.ElementId,
                Mark = s.Mark,
                CenterFt = [s.CenterXY.X, s.CenterXY.Y],
                WidthIn = s.WidthIn,
                DepthIn = s.DepthIn,
            }).ToList(),
            WallsBounding = GroupEdges(ctx, EdgeKind.Wall),
            Beams = GroupEdges(ctx, EdgeKind.Beam),
            SlabsCoplanar = GroupEdges(ctx, EdgeKind.Slab),
        };
        return info;
    }

    private static List<NeighborGroupInfo> GroupEdges(SlabContext ctx, EdgeKind kind) =>
        ctx.Edges
            .Where(e => e.Kind == kind && e.AdjacentElementId != 0)
            .GroupBy(e => e.AdjacentElementId)
            .Select(g => new NeighborGroupInfo
            {
                ElementId = g.Key,
                Mark = g.First().AdjacentMark,
                BoundaryIndices = g.Select(e => e.Index).ToList(),
            })
            .ToList();

    private static HintsInfo BuildHints(SlabGeometry geom, SlabContext ctx, IReadOnlyList<SlabOpening> openings)
    {
        var trim = new List<int>();
        for (int i = 0; i < openings.Count; i++)
            if (openings[i].NeedsTrim) trim.Add(i + 1);

        double w = geom.Bounds.Width, h = geom.Bounds.Height;
        double maxSpan = Math.Max(w, h);
        double minSpan = Math.Max(1e-6, Math.Min(w, h));

        return new HintsInfo
        {
            FreeEdgeIndices = ctx.FreeEdgeIndices.ToList(),
            NeedsEdgeUBars = ctx.FreeEdgeIndices.Count > 0,
            OpeningsNeedTrim = trim,
            Supports = ctx.Supports
                .Where(s => s.Kind == SupportKind.Column)
                .Select(s => new SupportHintInfo { Mark = s.Mark, SuggestedStripWidthIn = Math.Max(24.0, s.WidthIn * 4.0) })
                .ToList(),
            MaxSpanFt = maxSpan,
            IsTwoWay = maxSpan / minSpan <= 2.0,
            RecommendedLayers = ["BottomX", "BottomY", "TopX", "TopY"],
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

    private void RegisterType(Floor floor, SlabInfo slab, Dictionary<string, FloorTypeInfo> reg)
    {
        if (_doc.GetElement(floor.GetTypeId()) is not FloorType ft) return;

        slab.Family = ft.FamilyName;
        slab.Type = ft.Name;

        string key = ft.Id.Value.ToString();
        if (reg.ContainsKey(key)) return;

        reg[key] = new FloorTypeInfo
        {
            Family = ft.FamilyName,
            Type = ft.Name,
            Id = ft.Id.Value,
            ThicknessIn = slab.ThicknessIn,
            StructuralMaterial = MaterialName(ft),
        };
    }

    // ── Small helpers ────────────────────────────────────────────────────────────

    private LevelRef? LevelRefOf(Floor floor)
    {
        if (floor.LevelId == ElementId.InvalidElementId) return null;
        return _doc.GetElement(floor.LevelId) is not Level lvl
            ? null
            : new LevelRef { Name = lvl.Name, Id = lvl.Id.Value, ElevationFt = lvl.Elevation };
    }

    private CoverInfo ReadCover(Floor floor) => new()
    {
        Top = ReadCoverFace(floor, BuiltInParameter.CLEAR_COVER_TOP),
        Bottom = ReadCoverFace(floor, BuiltInParameter.CLEAR_COVER_BOTTOM),
    };

    private CoverFace? ReadCoverFace(Floor floor, BuiltInParameter bip)
    {
        ElementId id = floor.get_Parameter(bip)?.AsElementId() ?? ElementId.InvalidElementId;
        if (id == ElementId.InvalidElementId) return null;
        if (_doc.GetElement(id) is not RebarCoverType cover) return null;
        return new CoverFace { Name = cover.Name, DistanceIn = UnitConv.FtToIn(cover.CoverDistance) };
    }

    private string? MaterialName(FloorType ft)
    {
        ElementId id = ft.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)?.AsElementId()
                       ?? ElementId.InvalidElementId;
        return id != ElementId.InvalidElementId && _doc.GetElement(id) is Material m ? m.Name : null;
    }

    private static BboxInfo BoundsToBbox(Bounds2 b) => new()
    {
        MinFt = [b.MinX, b.MinY],
        MaxFt = [b.MaxX, b.MaxY],
    };

    private static List<EdgeSegmentInfo> LoopToSegments(Loop2 loop)
    {
        IReadOnlyList<Pt2> pts = loop.Points;
        int n = pts.Count;
        var segs = new List<EdgeSegmentInfo>(n);
        for (int i = 0; i < n; i++)
        {
            Pt2 a = pts[i], b = pts[(i + 1) % n];
            segs.Add(new EdgeSegmentInfo
            {
                Index = i,
                StartFt = [a.X, a.Y],
                EndFt = [b.X, b.Y],
                LengthFt = (b - a).Length,
            });
        }
        return segs;
    }

    private static string? MarkOf(Element e) => StringParam(e, BuiltInParameter.ALL_MODEL_MARK);

    private static string? StringParam(Element e, BuiltInParameter bip)
    {
        string? v = e.get_Parameter(bip)?.AsString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
