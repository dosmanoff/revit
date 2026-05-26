using Autodesk.Revit.UI;
using ColumnReinforcement.Domain;
using System.Text;

namespace ColumnReinforcement.UI;

public static class ResultsDialog
{
    public static void Show(RunResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.DryRun
            ? "DRY RUN — nothing was committed."
            : "Run complete.");
        sb.AppendLine();
        sb.AppendLine($"Succeeded: {result.Succeeded}");
        sb.AppendLine($"Skipped:   {result.Skipped}");
        sb.AppendLine($"Failed:    {result.Failed}");
        sb.AppendLine($"Bars created:  {result.TotalCreated}");
        sb.AppendLine($"Bars replaced: {result.TotalReplaced}");

        // Show notes for every column that has a Reason, regardless of overall status.
        // A column can succeed overall (longitudinals + ties placed) while some
        // sub-step (dowels, splices) was skipped for a reportable reason — without
        // surfacing those reasons here, the user has no way to see why they didn't
        // get the bars they enabled in the config.
        var notes = result.Outcomes
            .Where(o => !string.IsNullOrEmpty(o.Reason))
            .Take(10)
            .ToList();

        if (notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(notes.Count == result.Failed
                ? "Issues (first 10):"
                : "Notes (first 10):");
            foreach (var n in notes)
            {
                string prefix = n.Status switch
                {
                    ColumnStatus.Success => "ⓘ",
                    ColumnStatus.Skipped => "—",
                    ColumnStatus.Failed  => "✗",
                    _ => "·",
                };
                sb.AppendLine($"  {prefix} Column {n.ColumnId.Value}: {n.Reason}");
            }
        }

        var td = new TaskDialog("Column Reinforcement")
        {
            MainInstruction = result.DryRun ? "Dry-run summary" : "Run summary",
            MainContent     = sb.ToString(),
            CommonButtons   = TaskDialogCommonButtons.Close,
        };
        td.Show();
    }
}
