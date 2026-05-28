using Autodesk.Revit.DB;

namespace SmartViews.Engine;

/// <summary>
/// Creates per-column rebar schedules filtered to a single Host Mark.
///
/// <para>Field availability differs between Revit versions and templates, so every
/// field is looked up by display name and silently skipped when absent — the builder
/// never throws for a missing field. Default field sets are a sensible starting point
/// and are expected to be tuned to the project's schedule template.</para>
/// </summary>
public sealed class ColumnScheduleBuilder
{
    private readonly Document _doc;

    public ColumnScheduleBuilder(Document doc) => _doc = doc;

    /// <summary>Rebar quantity schedule filtered to <paramref name="hostMark"/>.</summary>
    public ViewSchedule? BuildRebarSchedule(string hostMark, string name)
    {
        ViewSchedule schedule = ViewSchedule.CreateSchedule(
            _doc, new ElementId(BuiltInCategory.OST_Rebar));
        schedule.Name = name;

        ScheduleDefinition def = schedule.Definition;

        AddFirstAvailable(def, "Schedule Mark", "Bar Mark", "Mark");
        AddFirstAvailable(def, "Type", "Bar Type", "Family and Type");
        AddFirstAvailable(def, "Bar Diameter");
        AddFirstAvailable(def, "Shape");
        AddFirstAvailable(def, "Style");
        AddFirstAvailable(def, "Quantity");
        AddFirstAvailable(def, "Total Bar Length", "Bar Length");

        ApplyHostMarkFilter(def, hostMark);
        return schedule;
    }

    /// <summary>
    /// Bending-detail schedule filtered to <paramref name="hostMark"/>: shape, shape image
    /// (when the template exposes it), diameter, the A–H bend dimensions, length and quantity.
    /// The graphical auto-dimensioned bending-detail column is a UI-only schedule option in
    /// Revit and cannot be toggled through the API, so this provides the tabular equivalent.
    /// </summary>
    public ViewSchedule? BuildBendingSchedule(string hostMark, string name)
    {
        ViewSchedule schedule = ViewSchedule.CreateSchedule(
            _doc, new ElementId(BuiltInCategory.OST_Rebar));
        schedule.Name = name;

        ScheduleDefinition def = schedule.Definition;

        AddFirstAvailable(def, "Schedule Mark", "Bar Mark", "Mark");
        AddFirstAvailable(def, "Shape");
        AddFirstAvailable(def, "Shape Image");
        AddFirstAvailable(def, "Bar Diameter");

        // Shape bend dimensions are exposed as single-letter fields when present.
        foreach (string dim in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "O", "R" })
            AddFirstAvailable(def, dim);

        AddFirstAvailable(def, "Total Bar Length", "Bar Length");
        AddFirstAvailable(def, "Quantity");

        ApplyHostMarkFilter(def, hostMark);
        return schedule;
    }

    // -----------------------------------------------------------------------

    private void ApplyHostMarkFilter(ScheduleDefinition def, string hostMark)
    {
        ScheduleField? hostMarkField = AddFirstAvailable(def, "Host Mark");
        if (hostMarkField is null)
            return;

        // Filter the schedule to the target column, then drop the now-redundant column.
        try
        {
            def.AddFilter(new ScheduleFilter(
                hostMarkField.FieldId, ScheduleFilterType.Equal, hostMark));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Field does not support an equality filter — leave the column visible instead.
            return;
        }

        hostMarkField.IsHidden = true;
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
