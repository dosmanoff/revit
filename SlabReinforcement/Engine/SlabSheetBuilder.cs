using Autodesk.Revit.DB;
using SlabReinforcement.Config;
using View = Autodesk.Revit.DB.View;

namespace SlabReinforcement.Engine;

/// <summary>
/// Creates a sheet per slab and lays the four layer plans out on a 2×2 grid with the rebar
/// schedule stacked on the right. Mirrors SmartViews.ColumnSheetBuilder.
/// </summary>
public sealed class SlabSheetBuilder
{
    private readonly Document _doc;
    private readonly SlabViewsConfig _cfg;

    public SlabSheetBuilder(Document doc, SlabViewsConfig cfg)
    {
        _doc = doc;
        _cfg = cfg;
    }

    public ViewSheet CreateSheet(string number, string name)
    {
        ViewSheet sheet = ViewSheet.Create(_doc, ResolveTitleBlock());
        sheet.SheetNumber = number;
        sheet.Name = name;
        return sheet;
    }

    public void PlaceOnSheet(ViewSheet sheet, IReadOnlyList<View> views, IReadOnlyList<ViewSchedule> schedules)
    {
        BoundingBoxUV ol = sheet.Outline;
        double minU = ol.Min.U, maxV = ol.Max.V;
        double w = ol.Max.U - ol.Min.U, h = ol.Max.V - ol.Min.V;
        const double m = 0.12;

        // Viewports on a fixed grid in the left ~62% of the sheet (no GetBoxOutline — it returns
        // null for not-yet-regenerated section/drafting viewports and NREs). Schedules go right.
        List<View> placeable = views.Where(v => Viewport.CanAddViewToSheet(_doc, sheet.Id, v.Id)).ToList();
        double vpAreaW = (w - 2 * m) * 0.62;
        const int cols = 2;
        int rows = Math.Max(3, (int)Math.Ceiling(placeable.Count / (double)cols));
        double cw = vpAreaW / cols, ch = (h - 2 * m) / rows;

        for (int i = 0; i < placeable.Count; i++)
        {
            Viewport vp = Viewport.Create(_doc, sheet.Id, placeable[i].Id, XYZ.Zero);
            int r = i / cols, c = i % cols;
            try { vp.SetBoxCenter(new XYZ(minU + m + c * cw + cw / 2.0, maxV - m - r * ch - ch / 2.0, 0)); }
            catch { /* off-sheet view — leave at origin */ }
        }

        _doc.Regenerate();

        // Schedules stacked top→down on the right; wrap into a second sub-column after a few.
        double sx0 = minU + m + vpAreaW + 0.15;
        const double colPitch = 1.0, rowGap = 0.6;
        int perCol = Math.Max(4, rows);
        for (int i = 0; i < schedules.Count; i++)
        {
            double sx = sx0 + (i / perCol) * colPitch;
            double sy = maxV - m - (i % perCol) * rowGap;
            try { ScheduleSheetInstance.Create(_doc, sheet.Id, schedules[i].Id, new XYZ(sx, sy, 0)); }
            catch { /* best-effort */ }
        }
    }

    private ElementId ResolveTitleBlock()
    {
        List<FamilySymbol> titleBlocks = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .ToList();
        if (titleBlocks.Count == 0) return ElementId.InvalidElementId;

        FamilySymbol symbol = (_cfg.TitleBlockName is not null
            ? titleBlocks.FirstOrDefault(t => string.Equals(t.Name, _cfg.TitleBlockName, StringComparison.OrdinalIgnoreCase))
            : null) ?? titleBlocks[0];

        if (!symbol.IsActive) symbol.Activate();
        return symbol.Id;
    }
}
