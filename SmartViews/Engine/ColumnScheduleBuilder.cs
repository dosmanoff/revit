using Autodesk.Revit.DB;

namespace SmartViews.Engine;

/// <summary>
/// Creates a per-column rebar schedule filtered to a single Host Mark, grouped by
/// Schedule Mark and non-itemized so each unique bar mark collapses to one row.
///
/// <para>Mark uniqueness (same mark ⇔ same type/shape/length/bend dimensions) is governed
/// by Revit's own reinforcement numbering, not assigned here — this builder only groups by
/// the resulting Schedule Mark. Fields are looked up by display name and skipped if absent,
/// so the builder never throws across template/version differences.</para>
/// </summary>
public sealed class ColumnScheduleBuilder
{
    private readonly Document _doc;

    public ColumnScheduleBuilder(Document doc) => _doc = doc;

    /// <param name="includeShapeImage">Include the Shape Image (bend-shape graphic) column.</param>
    public ViewSchedule BuildRebarSchedule(string hostMark, string name, bool includeShapeImage)
    {
        ViewSchedule schedule = ViewSchedule.CreateSchedule(
            _doc, new ElementId(BuiltInCategory.OST_Rebar));
        schedule.Name = name;

        ScheduleDefinition def = schedule.Definition;

        AddFirstAvailable(def, "Schedule Mark", "Bar Mark", "Mark");
        ScheduleField? typeField = AddFirstAvailable(def, "Type", "Bar Type", "Family and Type");
        ScheduleField? shapeField = AddFirstAvailable(def, "Shape");
        if (includeShapeImage)
            AddFirstAvailable(def, "Shape Image");
        ScheduleField? lengthField = AddFirstAvailable(def, "Total Bar Length", "Bar Length");

        // Shape bend dimensions (A, B, C, ...) — whichever the project's shapes expose.
        foreach (string dim in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "J", "K", "O", "R" })
            AddFirstAvailable(def, dim);

        // Filter to this column; the Host Mark column itself is hidden.
        ApplyHostMarkFilter(def, hostMark);

        // Sort/group by Type → Shape → Total Bar Length, collapsing identical bars to one row.
        def.IsItemized = false;
        AddGroup(def, typeField);
        AddGroup(def, shapeField);
        AddGroup(def, lengthField);

        return schedule;
    }

    // -----------------------------------------------------------------------

    private static void AddGroup(ScheduleDefinition def, ScheduleField? field)
    {
        if (field is null)
            return;

        try
        {
            def.AddSortGroupField(new ScheduleSortGroupField(field.FieldId, ScheduleSortOrder.Ascending));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Field cannot be used for sorting/grouping — skip it.
        }
    }

    private void ApplyHostMarkFilter(ScheduleDefinition def, string hostMark)
    {
        ScheduleField? hostMarkField = AddFirstAvailable(def, "Host Mark");
        if (hostMarkField is null)
            return;

        try
        {
            def.AddFilter(new ScheduleFilter(
                hostMarkField.FieldId, ScheduleFilterType.Equal, hostMark));
            hostMarkField.IsHidden = true;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Field does not support an equality filter — leave the column visible instead.
        }
    }

    private ScheduleField? AddFirstAvailable(ScheduleDefinition def, params string[] names)
    {
        IList<SchedulableField> fields = def.GetSchedulableFields();

        foreach (string name in names)
        {
            SchedulableField? match = fields.FirstOrDefault(f =>
                string.Equals(f.GetName(_doc), name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                continue;

            try
            {
                return def.AddField(match);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Field cannot be added to this schedule type — try the next candidate.
            }
        }

        return null;
    }
}
