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

        var problems = result.Outcomes
            .Where(o => o.Status != ColumnStatus.Success && !string.IsNullOrEmpty(o.Reason))
            .Take(10)
            .ToList();

        if (problems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Issues (first 10):");
            foreach (var p in problems)
                sb.AppendLine($"  · Column {p.ColumnId.Value}: {p.Reason}");
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
