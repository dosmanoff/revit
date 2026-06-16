using Autodesk.Revit.DB;
using StairsReinforcement.Geometry;

namespace StairsReinforcement.Domain;

public enum StairSourceKind { NativeStairs, Floors }

/// <summary>A support found at a flight end or under a landing edge.</summary>
public sealed class SupportInfo
{
    public string Kind { get; set; } = "none";   // slab | beam | wall | foundation | landing | stairs | none
    public long ElementId { get; set; }
    public double ElevationFt { get; set; }
}

/// <summary>An inclined flight waist — the primary span element of a stair.</summary>
public sealed class FlightComponent
{
    public int Index { get; set; }
    public ElementId ComponentId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
    public Element Host { get; set; } = null!;     // rebar host (Stairs or Floor)
    public bool RebarHostOk { get; set; }
    public string SourceKind { get; set; } = "run"; // "run" | "floor"

    public FlightFrame Frame { get; set; } = null!;
    public double WaistFt { get; set; }
    public double WidthFt { get; set; }
    public double HorizRunFt { get; set; }
    public double SlopeLengthFt { get; set; }
    public double TotalRiseFt { get; set; }
    public double SlopeRad { get; set; }
    public int RiserCount { get; set; }
    public int TreadCount { get; set; }
    public double TreadFt { get; set; }
    public double RiserFt { get; set; }
    public Bounds3 Bounds { get; set; } = new();

    public SupportInfo? LowerSupport { get; set; }
    public SupportInfo? UpperSupport { get; set; }
}

/// <summary>A horizontal landing slab connecting flights / floor levels.</summary>
public sealed class LandingComponent
{
    public int Index { get; set; }
    public ElementId ComponentId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
    public Element Host { get; set; } = null!;
    public bool RebarHostOk { get; set; }
    public string SourceKind { get; set; } = "landing"; // "landing" | "floor"

    public double ThicknessFt { get; set; }
    public double ElevationFt { get; set; }
    public double AreaSf { get; set; }
    public PlanBasis Basis { get; set; } = null!;
    public List<Pt2> Boundary { get; set; } = new();
    public Bounds3 Bounds { get; set; } = new();
    public List<SupportInfo> Supports { get; set; } = new();
    public List<int> ConnectsFlights { get; set; } = new();
}

/// <summary>One logical stair, resolved from a native <c>Stairs</c> element or a set of floors.</summary>
public sealed class StairAssembly
{
    public ElementId Id { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
    public StairSourceKind Source { get; set; }
    public Element HostElement { get; set; } = null!;   // representative, for Mark/Comments
    public string? Mark { get; set; }
    public string? Comments { get; set; }

    public List<FlightComponent> Flights { get; set; } = new();
    public List<LandingComponent> Landings { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public int ComponentCount => Flights.Count + Landings.Count;
}
