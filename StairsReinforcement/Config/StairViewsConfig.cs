using Autodesk.Revit.DB;

namespace StairsReinforcement.Config;

public enum ViewDuplicateHandling { Skip, Overwrite, AppendSuffix }

/// <summary>
/// Settings for the Stair Views command — a plan + longitudinal section(s) per stair, a rebar schedule,
/// annotations, and a sheet. Persisted in the document via <see cref="StairViewsConfigStore"/>.
/// New fields default to the 435E etalon conventions so an old stored config still deserializes.
/// </summary>
public sealed class StairViewsConfig
{
    public int SchemaVersion { get; set; } = 2;

    // ── Crop ────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Crop offset (ft) added around the stair extents on every plan/section.</summary>
    public double CropPadding { get; set; } = 1.0;

    // ── Section(s) ──────────────────────────────────────────────────────────────────────────────
    public int ViewScale { get; set; } = 48;                  // 1/4" = 1'-0"
    public ViewDetailLevel DetailLevel { get; set; } = ViewDetailLevel.Fine;
    public DisplayStyle VisualStyle { get; set; } = DisplayStyle.HLR;
    public string ViewNameTemplate { get; set; } = "{Mark} - Stair Section";
    /// <summary>ViewFamilyType for the section — defaults to Building Section so the marker shows on plan.</summary>
    public string? SectionViewTypeName { get; set; } = "Building Section";
    public string? ViewTemplateName { get; set; }

    /// <summary>Add a second section along a flight that is NOT parallel to the first (e.g. an L-stair).</summary>
    public bool SecondSectionWhenNotParallel { get; set; } = true;
    /// <summary>Two flights count as parallel when their plan run-axes are within this angle (degrees).</summary>
    public double ParallelToleranceDeg { get; set; } = 15.0;
    public string SecondSectionNameTemplate { get; set; } = "{Mark} - Stair Section 2";

    // ── Plan ────────────────────────────────────────────────────────────────────────────────────
    public bool CreatePlan { get; set; } = true;
    public string PlanNameTemplate { get; set; } = "{Mark} - Stair Plan";
    /// <summary>ViewFamilyType for the plan — null prefers a Structural Plan type, else a Floor Plan.</summary>
    public string? PlanViewTypeName { get; set; }
    public int PlanScale { get; set; } = 48;
    public string? PlanViewTemplateName { get; set; }
    /// <summary>Cut-plane height (ft) above the stair's base level — high enough to cut through the flights.</summary>
    public double PlanCutOffsetFt { get; set; } = 4.0;

    // ── Rebar display (applied to every plan + section) ─────────────────────────────────────────
    /// <summary>Hide rebar that doesn't belong to this stair (by the STR:{config}:{stairId} tag).</summary>
    public bool HideForeignRebar { get; set; } = true;
    /// <summary>Draw this stair's own bars unobscured so the cage reads through the concrete.</summary>
    public bool OwnRebarUnobscured { get; set; } = true;
    /// <summary>Show multi-bar sets as First/Last (the two end bars) instead of every bar.</summary>
    public bool RebarFirstLast { get; set; } = true;

    // ── Schedule ────────────────────────────────────────────────────────────────────────────────
    public bool CreateSchedule { get; set; } = true;
    public string ScheduleNameTemplate { get; set; } = "{Mark} - Rebar Schedule";

    // ── Annotations ─────────────────────────────────────────────────────────────────────────────
    /// <summary>Place a rebar tag (bar mark) on each of this stair's own sets.</summary>
    public bool CreateTags { get; set; } = true;
    /// <summary>Rebar-tag family type by name — null uses the first available structural rebar tag.</summary>
    public string? RebarTagTypeName { get; set; }
    /// <summary>
    /// Place a MultiReferenceAnnotation (tag + spacing dimension) on each in-plane distributed set. The MRA
    /// must declare its targets via <c>SetElementsToDimension</c> (geometry-only options auto-detect nothing),
    /// and the line is anchored from the set's world bbox — NOT the bar-relative GetBarPositionTransform origin.
    /// </summary>
    public bool CreateSpacingAnnotations { get; set; } = true;
    /// <summary>MultiReferenceAnnotationType by name — null prefers a "Slabs" type (matches the 435E etalon), else the first.</summary>
    public string? MraTypeName { get; set; }
    /// <summary>Place a rebar bending-detail (bent-shape diagram with A/B dims) per unique Schedule Mark in each section.</summary>
    public bool CreateBendingDetails { get; set; } = true;
    /// <summary>RebarBendingDetailType by name — null uses the first available.</summary>
    public string? BendingDetailTypeName { get; set; }

    // ── Sheet ───────────────────────────────────────────────────────────────────────────────────
    public bool PlaceOnSheet { get; set; } = true;
    public string? TitleBlockName { get; set; }
    public string SheetNumberTemplate { get; set; } = "S-{Mark}";
    public string SheetNameTemplate { get; set; } = "Stair {Mark} Reinforcement";
    public ViewDuplicateHandling DuplicateHandling { get; set; } = ViewDuplicateHandling.AppendSuffix;

    public static StairViewsConfig Default() => new();
}
