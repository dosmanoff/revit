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
        BoundingBoxUV outline = sheet.Outline;
        const double gap = 0.08;
        double left = outline.Min.U + 0.1;
        double topY = outline.Max.V - 0.1;

        var vps = new List<Viewport>();
        foreach (View view in views)
            if (Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id))
                vps.Add(Viewport.Create(_doc, sheet.Id, view.Id, XYZ.Zero));

        _doc.Regenerate();

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
        if (titleBlocks.Count == 0) return ElementId.InvalidElementId;

        FamilySymbol symbol = (_cfg.TitleBlockName is not null
            ? titleBlocks.FirstOrDefault(t => string.Equals(t.Name, _cfg.TitleBlockName, StringComparison.OrdinalIgnoreCase))
            : null) ?? titleBlocks[0];

        if (!symbol.IsActive) symbol.Activate();
        return symbol.Id;
    }
}
