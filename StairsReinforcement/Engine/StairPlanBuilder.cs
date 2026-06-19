using Autodesk.Revit.DB;
using StairsReinforcement.Config;
using View = Autodesk.Revit.DB.View;

namespace StairsReinforcement.Engine;

/// <summary>
/// Creates a structural plan view cropped to one stair, with the cut plane raised through the flights so
/// the run reads, scaled and set to Fine detail. Ported from SmartViews.Engine.ViewCreationEngine.CreatePlan.
/// </summary>
public sealed class StairPlanBuilder
{
    private readonly Document _doc;
    private readonly StairViewsConfig _cfg;

    public StairPlanBuilder(Document doc, StairViewsConfig cfg)
    {
        _doc = doc;
        _cfg = cfg;
    }

    /// <param name="min">World-aligned overall stair min (ft).</param>
    /// <param name="max">World-aligned overall stair max (ft).</param>
    public ViewPlan Create(XYZ min, XYZ max, string name)
    {
        Level level = BaseLevel(min.Z);
        ViewFamilyType vft = FindPlanViewFamilyType(_cfg.PlanViewTypeName);

        ViewPlan plan = ViewPlan.Create(_doc, vft.Id, level.Id);
        plan.Name = name;
        TrySet(() => plan.Scale = _cfg.PlanScale);
        TrySet(() => plan.DetailLevel = _cfg.DetailLevel);

        double pad = _cfg.CropPadding;
        BoundingBoxXYZ existing = plan.CropBox;        // keep its (world-aligned) transform and Z span
        plan.CropBoxActive = true;
        plan.CropBoxVisible = true;
        plan.CropBox = new BoundingBoxXYZ
        {
            Transform = existing.Transform,
            Min = new XYZ(min.X - pad, min.Y - pad, existing.Min.Z),
            Max = new XYZ(max.X + pad, max.Y + pad, existing.Max.Z),
        };

        // View range relative to the base level: cut partway up the flights, look from above the top to
        // below the soffit so the whole stair is within depth.
        try
        {
            double baseZ = level.Elevation;
            PlanViewRange vr = plan.GetViewRange();
            vr.SetLevelId(PlanViewPlane.TopClipPlane, level.Id);
            vr.SetOffset(PlanViewPlane.TopClipPlane, (max.Z - baseZ) + pad);
            vr.SetLevelId(PlanViewPlane.CutPlane, level.Id);
            vr.SetOffset(PlanViewPlane.CutPlane, _cfg.PlanCutOffsetFt);
            vr.SetLevelId(PlanViewPlane.BottomClipPlane, level.Id);
            vr.SetOffset(PlanViewPlane.BottomClipPlane, (min.Z - baseZ) - pad);
            vr.SetLevelId(PlanViewPlane.ViewDepthPlane, level.Id);
            vr.SetOffset(PlanViewPlane.ViewDepthPlane, (min.Z - baseZ) - pad);
            plan.SetViewRange(vr);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { }

        return plan;
    }

    /// <summary>The level at or just below the stair's base (greatest elevation ≤ baseZ); else the lowest.</summary>
    private Level BaseLevel(double baseZ)
    {
        List<Level> levels = new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation).ToList();
        if (levels.Count == 0)
            throw new InvalidOperationException("No Level in the document to host a stair plan.");
        Level? at = levels.LastOrDefault(l => l.Elevation <= baseZ + 1.0 / 12.0);
        return at ?? levels[0];
    }

    /// <summary>Prefer a Structural Plan type (by name, else first), else any Floor Plan type.</summary>
    private ViewFamilyType FindPlanViewFamilyType(string? preferred)
    {
        List<ViewFamilyType> all = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToList();

        List<ViewFamilyType> structural = all.Where(t => t.ViewFamily == ViewFamily.StructuralPlan).ToList();
        List<ViewFamilyType> floor = all.Where(t => t.ViewFamily == ViewFamily.FloorPlan).ToList();
        List<ViewFamilyType> pool = structural.Count > 0 ? structural : floor;
        if (pool.Count == 0)
            throw new InvalidOperationException("No Structural Plan or Floor Plan ViewFamilyType in the document.");

        if (preferred is not null)
        {
            ViewFamilyType? hit = pool.FirstOrDefault(t => string.Equals(t.Name, preferred, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(t => (t.ViewFamily == ViewFamily.StructuralPlan || t.ViewFamily == ViewFamily.FloorPlan)
                    && string.Equals(t.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return pool[0];
    }

    private static void TrySet(Action set)
    {
        try { set(); }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
    }
}
