using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;
using StairsReinforcement.Engine;
using StairsReinforcement.UI;

namespace StairsReinforcement.Commands;

/// <summary>
/// Generates stair rebar from a per-stair assignments CSV (matched by Mark) or one JSON config for
/// all selected stairs. Re-running a config replaces its prior result (idempotent by STR tag).
/// </summary>
[Transaction(TransactionMode.Manual)]
public class GenerateStairsRebarCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        var filter = new StairsSelectionFilter();
        List<Element> picked = uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id)).Where(filter.AllowElement).ToList();
        if (picked.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element, filter, "Select stairs to reinforce, then click Finish");
                picked = refs.Select(r => doc.GetElement(r.ElementId)).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }
        if (picked.Count == 0)
        {
            TaskDialog.Show("Generate Stair Rebar", "No stairs or structural floors selected.");
            return Result.Cancelled;
        }

        var dlg = new StairsRebarGenDialog(doc);
        if (dlg.ShowDialog() != true) return Result.Cancelled;

        PersistPaths(doc, dlg);

        List<StairAssembly> assemblies = StairSourceResolver.Resolve(doc, picked);
        var skipped = new List<string>();
        List<(StairAssembly, StairsReinforcementConfig)> work;
        try { work = BuildWork(assemblies, dlg, skipped); }
        catch (Exception ex) { message = ex.Message; return Result.Failed; }

        if (work.Count == 0)
        {
            TaskDialog.Show("Generate Stair Rebar",
                "Nothing to do — no stair matched a config.\n\n" + string.Join("\n", skipped));
            return Result.Cancelled;
        }

        RunResult result = new StairsReinforcer(doc).Run(work, dlg.DryRun);
        TaskDialog.Show("Generate Stair Rebar", Summarize(result, skipped));
        return Result.Succeeded;
    }

    private static List<(StairAssembly, StairsReinforcementConfig)> BuildWork(
        IReadOnlyList<StairAssembly> assemblies, StairsRebarGenDialog dlg, List<string> skipped)
    {
        var work = new List<(StairAssembly, StairsReinforcementConfig)>();

        if (dlg.FromCsv)
        {
            if (dlg.CsvPath is null || !File.Exists(dlg.CsvPath))
                throw new InvalidOperationException("Choose an existing assignments CSV.");

            AssignmentTable table = AssignmentCsv.Parse(File.ReadAllText(dlg.CsvPath), dlg.CsvPath);
            foreach (string issue in table.Issues.Take(5).Select(i => $"CSV line {i.Line}: {i.Field} — {i.Message}"))
                skipped.Add(issue);

            foreach (StairAssembly asm in assemblies)
            {
                StairsReinforcementConfig? cfg = table.TryGetConfig(asm.Mark);
                if (cfg is null) skipped.Add($"Mark '{asm.Mark ?? "—"}' (stair {asm.Id.Value}): no CSV row");
                else work.Add((asm, cfg));
            }
        }
        else
        {
            if (dlg.ConfigPath is null || !File.Exists(dlg.ConfigPath))
                throw new InvalidOperationException("Choose a config JSON file.");

            StairsReinforcementConfig cfg = ConfigLoader.Load(dlg.ConfigPath);
            foreach (StairAssembly asm in assemblies) work.Add((asm, cfg));
        }

        return work;
    }

    private static void PersistPaths(Document doc, StairsRebarGenDialog dlg)
    {
        try
        {
            using var tx = new Transaction(doc, "Stair rebar settings");
            tx.Start();
            if (dlg.ConfigFolder is { } f) FolderStorage.SetConfigFolder(doc, f);
            if (dlg.CsvPath is { } c) FolderStorage.SetCsvPath(doc, c);
            tx.Commit();
        }
        catch { /* persistence is best-effort */ }
    }

    private static string Summarize(RunResult result, List<string> skipped)
    {
        var sb = new StringBuilder();
        if (result.DryRun) sb.AppendLine("DRY RUN — nothing was placed.\n");

        sb.AppendLine($"Succeeded: {result.Succeeded}   Skipped: {result.Skipped}   Failed: {result.Failed}");
        sb.AppendLine($"Bars created: {result.TotalCreated}   replaced: {result.TotalReplaced}");

        foreach (StairOutcome o in result.Outcomes.Where(o => o.Status != StairStatus.Success).Take(12))
            sb.AppendLine($"  • {o.Mark} (stair {o.StairId}): {o.Status} — {o.Reason}");

        if (skipped.Count > 0)
        {
            sb.AppendLine();
            foreach (string s in skipped.Take(12)) sb.AppendLine($"  – {s}");
        }
        return sb.ToString();
    }
}
