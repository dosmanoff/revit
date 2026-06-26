using Autodesk.Revit.DB;

namespace WallReinforcement.Config;

public enum RebarIsolation { Hide, Halftone, Show }

/// <summary>
/// Settings for the Wall Views command — per-wall face elevations (exterior + interior), a
/// horizontal section through the thickness, an optional 3D cage, a rebar schedule and a sheet.
/// Mirrors <c>SlabViewsConfig</c> but with wall-appropriate view types. Persisted on the document
/// via <see cref="WallViewsConfigStore"/>.
/// </summary>
public sealed class WallViewsConfig
{
    public int SchemaVersion { get; set; } = 1;

    public double CropPadding { get; set; } = 1.0;            // ft
    /// <summary>Auto-pick the view scale so the wall fits <see cref="TargetViewWidthIn"/>; when
    /// false, <see cref="Scale"/> is used as-is.</summary>
    public bool AutoScale { get; set; } = true;
    public int Scale { get; set; } = 24;                      // 1/2" = 1'-0"
    public double TargetViewWidthIn { get; set; } = 9.0;
    public ViewDetailLevel DetailLevel { get; set; } = ViewDetailLevel.Fine;
    public DisplayStyle VisualStyle { get; set; } = DisplayStyle.HLR;
    /// <summary>What to do with rebar that belongs to OTHER walls in each view.</summary>
    public RebarIsolation Isolation { get; set; } = RebarIsolation.Hide;
    /// <summary>Set the rebar sets to "Show middle rebar" presentation in the generated views.</summary>
    public bool ShowMiddleBarOnly { get; set; } = true;
    public string? ViewTemplateName { get; set; }
    public string? SectionViewTypeName { get; set; }

    // Face elevations — one looking at the exterior face, one at the interior face, each showing
    // that face's mesh + edge bars unobscured.
    public bool CreateElevations { get; set; } = true;
    public string ElevationNameTemplate { get; set; } = "{Mark} - {Face} Face";

    // Horizontal section through the wall at mid-height: the "plan" of the reinforcement — both
    // mats as lines, the transverse ties, cover.
    public bool CreateSection { get; set; } = true;
    public string SectionNameTemplate { get; set; } = "{Mark} - Section";
    public double SectionDepthFt { get; set; } = 1.0;

    // 3D isolated cage (optional, off by default).
    public bool Create3DView { get; set; } = false;
    public int View3DScale { get; set; } = 24;
    public string View3DNameTemplate { get; set; } = "{Mark} - 3D Cage";

    // Schedule + sheet.
    public bool CreateSchedule { get; set; } = true;
    public string ScheduleNameTemplate { get; set; } = "{Mark} - Rebar Schedule";
    public bool PlaceOnSheet { get; set; } = true;
    public string? TitleBlockName { get; set; }
    public string SheetNumberTemplate { get; set; } = "WR-{Mark}";
    public string SheetNameTemplate { get; set; } = "Wall {Mark} Reinforcement";

    public static WallViewsConfig Default() => new();
}
