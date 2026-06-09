using Autodesk.Revit.DB;
using SlabReinforcement.Domain;

namespace SlabReinforcement.Engine;

/// <summary>
/// Creates a rebar schedule for one slab LAYER, filtered by the SR tag <c>:{slabId}:{layer}</c>,
/// grouped by Schedule Mark and non-itemized so identical bars (same mark = same diameter / shape /
/// length) collapse to one row. Fields are looked up by display name and skipped if absent, so it
/// never throws across template/version differences. Mirrors SmartViews.ColumnScheduleBuilder.
/// </summary>
public sealed class SlabScheduleBuilder
{
    private readonly Document _doc;

    public SlabScheduleBuilder(Document doc) => _doc = doc;

    public ViewSchedule BuildRebarSchedule(ElementId slabId, SlabLayer layer, string name)
    {
        ViewSchedule schedule = ViewSchedule.CreateSchedule(_doc, new ElementId(BuiltInCategory.OST_Rebar));
        schedule.Name = name;

        ScheduleDefinition def = schedule.Definition;

        ScheduleField? markField = AddFirstAvailable(def, "Schedule Mark", "Bar Mark", "Mark");
        ScheduleField? typeField = AddFirstAvailable(def, "Type", "Bar Type", "Family and Type");
        ScheduleField? shapeField = AddFirstAvailable(def, "Shape");
        AddFirstAvailable(def, "Total Bar Length", "Bar Length");
        AddFirstAvailable(def, "Quantity");
        ScheduleField? commentsField = AddFirstAvailable(def, "Comments");   // holds the SR:{config}:{slabId}:{layer} tag

        // One schedule per layer: filter to this slab + layer via the tag.
        ApplyTagFilter(def, commentsField, slabId, layer);
        if (commentsField is not null) commentsField.IsHidden = true;

        def.IsItemized = false;
        AddGroup(def, markField);     // group by Schedule Mark → one row per unique bar
        AddGroup(def, typeField);
        AddGroup(def, shapeField);

        return schedule;
    }

    private static void AddGroup(ScheduleDefinition def, ScheduleField? field)
    {
        if (field is null) return;
        try { def.AddSortGroupField(new ScheduleSortGroupField(field.FieldId, ScheduleSortOrder.Ascending)); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { }
    }

    private static void ApplyTagFilter(ScheduleDefinition def, ScheduleField? commentsField, ElementId slabId, SlabLayer layer)
    {
        if (commentsField is null) return;
        // tag = SR:{config}:{slabId}:{layer}; ":{slabId}:{layer}" pins both slab and exact layer.
        try { def.AddFilter(new ScheduleFilter(commentsField.FieldId, ScheduleFilterType.Contains, $":{slabId.Value}:{layer}")); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { }
    }

    private ScheduleField? AddFirstAvailable(ScheduleDefinition def, params string[] names)
    {
        int existing = def.GetFieldCount();
        for (int i = 0; i < existing; i++)
        {
            ScheduleField f = def.GetField(i);
            foreach (string name in names)
                if (string.Equals(f.ColumnHeading, name, StringComparison.OrdinalIgnoreCase))
                    return f;
        }

        IList<SchedulableField> fields = def.GetSchedulableFields();
        foreach (string name in names)
        {
            SchedulableField? match = fields.FirstOrDefault(f =>
                string.Equals(f.GetName(_doc), name, StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;
            try { return def.AddField(match); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        }
        return null;
    }
}
