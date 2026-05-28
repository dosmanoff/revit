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
        double u0 = outline.Min.U, v0 = outline.Min.V;
        double w = outline.Max.U - u0;
        double h = outline.Max.V - v0;

        double[] colU = { u0 + w * 0.22, u0 + w * 0.52 };
        double[] rowV = { v0 + h * 0.72, v0 + h * 0.40 };

        for (int i = 0; i < views.Count; i++)
        {
            View view = views[i];
            if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id))
                continue;

            double u = colU[i % 2];
            double v = rowV[(i / 2) % 2];
            Viewport.Create(_doc, sheet.Id, view.Id, new XYZ(u, v, 0));
        }

        double scheduleU = u0 + w * 0.80;
        for (int i = 0; i < schedules.Count; i++)
        {
            double v = v0 + h * (0.80 - i * 0.35);
            ScheduleSheetInstance.Create(_doc, sheet.Id, schedules[i].Id, new XYZ(scheduleU, v, 0));
        }
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
