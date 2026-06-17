using System.Text.Json;
using System.Text.Json.Serialization;

namespace StairsReinforcement.Export;

/// <summary>
/// Pure serializable description of the selected stairs for the reinforcement agent. Keys are
/// snake_case (matching the Slab/Column dumps); units are encoded in the field-name suffixes —
/// sections/cover in inches (<c>*_in</c>), positions/elevations/areas in feet, angles in degrees.
/// No Revit types here — <see cref="StairsDumpBuilder"/> is the only Revit→DTO mapper.
/// </summary>
public class StairsDump
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public DocumentInfo Document { get; set; } = new();
    public string GeneratedAt { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string UnitsNote { get; set; } =
        "Sections & cover in inches (*_in); positions/elevations/areas in feet; angles in degrees from world +X. " +
        "Bar sizes & spacing come from you (with the engineer) — the plugin sizes nothing from loads.";

    public List<LevelInfo> Levels { get; set; } = new();
    public Dictionary<string, ElementTypeInfo> StairTypesInUse { get; set; } = new();
    public List<BarTypeInfo> AvailableRebarBarTypes { get; set; } = new();
    public List<HookTypeInfo> AvailableRebarHookTypes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<StairDto> Stairs { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public class DocumentInfo
{
    public string? Title { get; set; }
    public string? Path { get; set; }
}

public class LevelInfo
{
    public string Name { get; set; } = "";
    public double ElevationFt { get; set; }
    public long Id { get; set; }
}

public class BarTypeInfo
{
    public string Name { get; set; } = "";
    public double NominalDiameterIn { get; set; }
}

public class HookTypeInfo
{
    public string Name { get; set; } = "";
}

public class ElementTypeInfo
{
    public string? Family { get; set; }
    public string? Type { get; set; }
    public long Id { get; set; }
}

public class StairDto
{
    public long ElementId { get; set; }
    public string? Mark { get; set; }
    public string? Comments { get; set; }
    public string Source { get; set; } = "";   // "stairs" | "floors"
    public bool RebarHostOk { get; set; }
    public List<FlightDto> Flights { get; set; } = new();
    public List<LandingDto> Landings { get; set; } = new();
    public List<string>? Warnings { get; set; }
}

public class FlightDto
{
    public int Index { get; set; }
    public long ComponentId { get; set; }
    public string Source { get; set; } = "";   // "run" | "floor"
    public bool RebarHostOk { get; set; }

    public double WaistIn { get; set; }
    public double WidthFt { get; set; }
    public double RunLengthFt { get; set; }
    public double SlopeLengthFt { get; set; }
    public double TotalRiseFt { get; set; }
    public double SlopeDeg { get; set; }
    public int RiserCount { get; set; }
    public int TreadCount { get; set; }
    public double TreadIn { get; set; }
    public double RiserIn { get; set; }

    public FlightBasis LocalBasis { get; set; } = new();
    public BboxDto Bbox { get; set; } = new();
    public SupportDto? LowerSupport { get; set; }
    public SupportDto? UpperSupport { get; set; }
}

public class LandingDto
{
    public int Index { get; set; }
    public long ComponentId { get; set; }
    public string Source { get; set; } = "";   // "landing" | "floor"

    public double ThicknessIn { get; set; }
    public double ElevationFt { get; set; }
    public double AreaSf { get; set; }

    public PlanBasisDto LocalBasis { get; set; } = new();
    public BboxDto Bbox { get; set; } = new();
    public List<double[]> Boundary { get; set; } = new();   // plan loop [[x,y], …]
    public List<SupportDto> Supports { get; set; } = new();
    public List<int> ConnectsFlights { get; set; } = new();
}

public class FlightBasis
{
    public double[] OriginFt { get; set; } = new double[3];
    public double[] UDir { get; set; } = new double[3];   // up-slope axis
    public double[] WDir { get; set; } = new double[2];   // width axis (horizontal)
    public double[] NDir { get; set; } = new double[3];   // waist normal
    public double RunWorldDeg { get; set; }
}

public class PlanBasisDto
{
    public double[] OriginFt { get; set; } = new double[2];
    public double[] XDir { get; set; } = new double[2];
    public double[] YDir { get; set; } = new double[2];
    public double AngleWorldDeg { get; set; }
}

public class BboxDto
{
    public double[] MinFt { get; set; } = new double[3];
    public double[] MaxFt { get; set; } = new double[3];
}

public class SupportDto
{
    public string Kind { get; set; } = "none";
    public long ElementId { get; set; }
    public double ElevationFt { get; set; }
}
