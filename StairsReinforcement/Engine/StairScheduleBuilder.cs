using Autodesk.Revit.DB;

namespace StairsReinforcement.Engine;

/// <summary>
/// Creates a rebar schedule for one stair, filtered by Host Mark and non-itemized so identical
/// bars collapse to one row. Includes Comments so the STR: tag (with the layer) is visible. Fields
/// are looked up by display name and skipped if absent, so it never throws across template/version
/// differences. Ported from SlabReinforcement.Engine.SlabScheduleBuilder.
/// </summary>
public sealed class StairScheduleBuilder
{
    private readonly Document _doc;

    public StairScheduleBuilder(Document doc) => _doc = doc;

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
        AddFirstAvailable(def, "Comments");        // shows the STR:{config}:{stairId}:{layer} tag

        ApplyHostMarkFilter(def, hostMark);

        def.IsItemized = false;
        AddGroup(def, typeField);
        AddGroup(def, shapeField);
        AddGroup(def, lengthField);
        RoundLengthToHalfInch(lengthField);

        return schedule;
    }

    /// <summary>
    /// Round the bar-length column to the nearest 1/2" so near-equal cut lengths read as one clean value
    /// (the 435E etalon convention). <see cref="FormatOptions.Accuracy"/> is in internal units (feet), so
    /// 1/2" = <c>ConvertToInternalUnits(0.5, Inches)</c>; the display unit/symbol follow the project's
    /// length format. Best-effort — skipped if the field rejects a format override.
    /// </summary>
    private void RoundLengthToHalfInch(ScheduleField? lengthField)
    {
        if (lengthField is null) return;
        try
        {
            FormatOptions proj = _doc.GetUnits().GetFormatOptions(SpecTypeId.Length);
            var fo = new FormatOptions(proj.GetUnitTypeId());
            try { fo.SetSymbolTypeId(proj.GetSymbolTypeId()); } catch { }
            fo.Accuracy = UnitUtils.ConvertToInternalUnits(0.5, UnitTypeId.Inches);
            lengthField.SetFormatOptions(fo);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
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
        if (hostMarkField is null || string.IsNullOrWhiteSpace(hostMark)) return;
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
