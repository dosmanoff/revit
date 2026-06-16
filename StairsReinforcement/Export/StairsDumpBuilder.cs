using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StairsReinforcement.Domain;
using StairsReinforcement.Engine;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Export;

/// <summary>Maps resolved <see cref="StairAssembly"/> objects + document resources into a <see cref="StairsDump"/>.</summary>
public sealed class StairsDumpBuilder
{
    private readonly Document _doc;

    public StairsDumpBuilder(Document doc) => _doc = doc;

    public StairsDump Build(IReadOnlyList<StairAssembly> assemblies, string generatedAtIso)
    {
        var dump = new StairsDump
        {
            GeneratedAt = generatedAtIso,
            Document = new DocumentInfo { Title = _doc.Title, Path = string.IsNullOrEmpty(_doc.PathName) ? null : _doc.PathName },
            Levels = CollectLevels(),
            AvailableRebarBarTypes = CollectBarTypes(),
            AvailableRebarHookTypes = CollectHookTypes(),
        };

        foreach (StairAssembly asm in assemblies)
        {
            try { dump.Stairs.Add(BuildStair(asm, dump.StairTypesInUse)); }
            catch (Exception ex) { dump.Warnings.Add($"Stair {asm.Id.Value}: {ex.Message}"); }
        }

        return dump;
    }

    private StairDto BuildStair(StairAssembly asm, Dictionary<string, ElementTypeInfo> typesInUse)
    {
        RecordType(asm.HostElement, typesInUse);

        return new StairDto
        {
            ElementId = asm.Id.Value,
            Mark = asm.Mark,
            Comments = asm.Comments,
            Source = asm.Source == StairSourceKind.NativeStairs ? "stairs" : "floors",
            RebarHostOk = asm.Flights.All(f => f.RebarHostOk) && asm.Landings.All(l => l.RebarHostOk),
            Flights = asm.Flights.Select(BuildFlight).ToList(),
            Landings = asm.Landings.Select(BuildLanding).ToList(),
            Warnings = asm.Warnings.Count > 0 ? asm.Warnings : null,
        };
    }

    private FlightDto BuildFlight(FlightComponent f)
    {
        FlightFrame fr = f.Frame;
        var uHoriz = new Pt2(fr.U.X, fr.U.Y);
        return new FlightDto
        {
            Index = f.Index,
            ComponentId = f.ComponentId.Value,
            Source = f.SourceKind,
            RebarHostOk = f.RebarHostOk,
            WaistIn = UnitConv.FtToIn(f.WaistFt),
            WidthFt = f.WidthFt,
            RunLengthFt = f.HorizRunFt,
            SlopeLengthFt = f.SlopeLengthFt,
            TotalRiseFt = f.TotalRiseFt,
            SlopeDeg = UnitConv.Deg(f.SlopeRad),
            RiserCount = f.RiserCount,
            TreadCount = f.TreadCount,
            TreadIn = UnitConv.FtToIn(f.TreadFt),
            RiserIn = UnitConv.FtToIn(f.RiserFt),
            LocalBasis = new FlightBasis
            {
                OriginFt = Vec3(fr.Origin),
                UDir = Vec3(fr.U),
                WDir = new[] { fr.W.X, fr.W.Y },
                NDir = Vec3(fr.N),
                RunWorldDeg = UnitConv.Deg(Math.Atan2(uHoriz.Y, uHoriz.X)),
            },
            Bbox = Bbox(f.Bounds),
            LowerSupport = Support(f.LowerSupport),
            UpperSupport = Support(f.UpperSupport),
        };
    }

    private LandingDto BuildLanding(LandingComponent l) => new()
    {
        Index = l.Index,
        ComponentId = l.ComponentId.Value,
        Source = l.SourceKind,
        ThicknessIn = UnitConv.FtToIn(l.ThicknessFt),
        ElevationFt = l.ElevationFt,
        AreaSf = l.AreaSf,
        LocalBasis = new PlanBasisDto
        {
            OriginFt = new[] { l.Basis.Origin.X, l.Basis.Origin.Y },
            XDir = new[] { l.Basis.X.X, l.Basis.X.Y },
            YDir = new[] { l.Basis.Y.X, l.Basis.Y.Y },
            AngleWorldDeg = l.Basis.AngleDeg,
        },
        Bbox = Bbox(l.Bounds),
        Boundary = l.Boundary.Select(p => new[] { p.X, p.Y }).ToList(),
        Supports = l.Supports.Select(s => Support(s)!).ToList(),
        ConnectsFlights = l.ConnectsFlights,
    };

    // ── document resources ───────────────────────────────────────────────────

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

    private void RecordType(Element host, Dictionary<string, ElementTypeInfo> typesInUse)
    {
        ElementId typeId = host.GetTypeId();
        if (typeId == ElementId.InvalidElementId) return;
        string key = typeId.Value.ToString();
        if (typesInUse.ContainsKey(key)) return;

        if (_doc.GetElement(typeId) is ElementType et)
            typesInUse[key] = new ElementTypeInfo { Family = et.FamilyName, Type = et.Name, Id = typeId.Value };
    }

    // ── small mappers ────────────────────────────────────────────────────────

    private static double[] Vec3(Pt3 p) => new[] { p.X, p.Y, p.Z };

    private static BboxDto Bbox(Bounds3 b) => b.IsEmpty
        ? new BboxDto()
        : new BboxDto { MinFt = Vec3(b.Min), MaxFt = Vec3(b.Max) };

    private static SupportDto? Support(SupportInfo? s) => s is null
        ? null
        : new SupportDto { Kind = s.Kind, ElementId = s.ElementId, ElevationFt = s.ElevationFt };
}
