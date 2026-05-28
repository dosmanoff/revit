using Autodesk.Revit.DB;
using SmartViews.Config;
using View = Autodesk.Revit.DB.View;

namespace SmartViews.Engine;

/// <summary>
/// Creates a sheet per column and lays the generated graphical views and schedules out
/// on a simple grid. Viewport sizes are not known until placement, so the layout uses
/// fixed fractions of the sheet outline; users are expected to nudge items afterwards.
/// </summary>
public sealed class ColumnSheetBuilder
{
    private readonly Document _doc;
    private readonly ColumnViewsConfig _cfg;

    public ColumnSheetBuilder(Document doc, ColumnViewsConfig cfg)
    {
        _doc = doc;
        _cfg = cfg;
    }

    public ViewSheet CreateSheet(string number, string name)
    {
        ElementId titleBlockId = ResolveTitleBlock();

        ViewSheet sheet = ViewSheet.Create(_doc, titleBlockId);
        sheet.SheetNumber = number;
        sheet.Name = name;
        return sheet;
    }

    /// <summary>
    /// Places <paramref name="views"/> on a 2×2 grid (left two-thirds) and
    /// <paramref name="schedules"/> stacked down the right third.
    /// </summary>
    public void PlaceOnSheet(ViewSheet sheet, IReadOnlyList<View> views, IReadOnlyList<ViewSchedule> schedules)
    {
        BoundingBoxUV outline = sheet.Outline;
        const double gap = 0.08;                  // ~1" between items, in sheet feet
        double left = outline.Min.U + 0.1;
        double topY = outline.Max.V - 0.1;

        var vps = new List<Viewport>();
        foreach (View view in views)
        {
            if (Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id))
                vps.Add(Viewport.Create(_doc, sheet.Id, view.Id, XYZ.Zero));
        }

        // Viewport outlines are only valid once the new viewports are regenerated.
        _doc.Regenerate();

        // Pack two viewports per row using their true outline sizes so none overlap.
        double rightEdge = left;
        double rowTop = topY;
        for (int i = 0; i < vps.Count; i += 2)
        {
            List<Viewport> row = vps.Skip(i).Take(2).ToList();
            double rowHeight = row.Max(BoxHeight);
            double x = left;
            foreach (Viewport vp in row)
            {
                double w = BoxWidth(vp);
                vp.SetBoxCenter(new XYZ(x + w / 2.0, rowTop - rowHeight / 2.0, 0));
                x += w + gap;
                rightEdge = Math.Max(rightEdge, x);
            }
            rowTop -= rowHeight + gap;
        }

        // Schedules stack down the right of the viewport block.
        double scheduleX = rightEdge + gap;
        double scheduleY = topY;
        foreach (ViewSchedule schedule in schedules)
        {
            ScheduleSheetInstance.Create(_doc, sheet.Id, schedule.Id, new XYZ(scheduleX, scheduleY, 0));
            scheduleY -= 0.6;
        }
    }

    private static double BoxWidth(Viewport vp)
    {
        Outline o = vp.GetBoxOutline();
        return o.MaximumPoint.X - o.MinimumPoint.X;
    }

    private static double BoxHeight(Viewport vp)
    {
        Outline o = vp.GetBoxOutline();
        return o.MaximumPoint.Y - o.MinimumPoint.Y;
    }

    private ElementId ResolveTitleBlock()
    {
        List<FamilySymbol> titleBlocks = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .ToList();

        if (titleBlocks.Count == 0)
            return ElementId.InvalidElementId; // sheet without a title block

        FamilySymbol symbol = (_cfg.TitleBlockName is not null
            ? titleBlocks.FirstOrDefault(t =>
                string.Equals(t.Name, _cfg.TitleBlockName, StringComparison.OrdinalIgnoreCase))
            : null) ?? titleBlocks[0];

        if (!symbol.IsActive)
            symbol.Activate();

        return symbol.Id;
    }
}
