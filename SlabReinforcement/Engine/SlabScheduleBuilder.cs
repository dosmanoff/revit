using Autodesk.Revit.DB;

namespace SlabReinforcement.Engine;

/// <summary>
/// Creates a rebar schedule for one slab, filtered by Host Mark and non-itemized so identical
/// bars collapse to one row. Includes Comments so the SR: tag (with the layer) is visible.
/// Fields are looked up by display name and skipped if absent, so it never throws across
/// template/version differences. Mirrors SmartViews.ColumnScheduleBuilder.
/// </summary>
public sealed class SlabScheduleBuilder
{
    private readonly Document _doc;

    public SlabScheduleBuilder(Document doc) => _doc = doc;

    public ViewSchedule BuildRebarSchedule(string hostMark, string name)
    {
        ViewSchedule schedule = ViewSchedule.CreateSchedule(_doc, new ElementId(BuiltInCategory.OST_Rebar));
        schedule.Name = name;

        ScheduleDefinition def = schedule.Definition;

        AddFirstAvailable(def, "Schedule Mark", "Bar Mark", "Mark");
        ScheduleField? typeField = AddFirstAvailable(def, "Type", "Bar Type", "Family and Type");
        ScheduleField? shapeField = AddFirstAvailable(def, "Shape");
        ScheduleField? lengthField = AddFirstAvailable(def, "Total Bar Length", "Bar Length");
        AddFirstAvailable(def, "Quantity");
        AddFirstAvailable(def, "Comments");        // shows the SR:{config}:{slabId}:{layer} tag

        ApplyHostMarkFilter(def, hostMark);

        def.IsItemized = false;
        AddGroup(def, typeField);
        AddGroup(def, shapeField);
        AddGroup(def, lengthField);

        return schedule;
    }

    private static void AddGroup(ScheduleDefinition def, ScheduleField? field)
    {
        if (field is null) return;
        try { def.AddSortGroupField(new ScheduleSortGroupField(field.FieldId, ScheduleSortOrder.Ascending)); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { }
    }

    private void ApplyHostMarkFilter(ScheduleDefinition def, string hostMark)
    {
        ScheduleField? hostMarkField = AddFirstAvailable(def, "Host Mark");
        if (hostMarkField is null) return;
        try
        {
            def.AddFilter(new ScheduleFilter(hostMarkField.FieldId, ScheduleFilterType.Equal, hostMark));
            hostMarkField.IsHidden = true;
        }
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
