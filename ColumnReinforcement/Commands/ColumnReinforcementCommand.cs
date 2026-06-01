using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;
using ColumnReinforcement.Engine;
using ColumnReinforcement.UI;

namespace ColumnReinforcement.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ColumnReinforcementCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        IList<ElementId> columnIds = GetSelectedColumnIds(uidoc);
        if (columnIds.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new ColumnSelectionFilter(),
                    "Pick structural columns, then press Finish");
                columnIds = refs.Select(r => r.ElementId).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }

        if (columnIds.Count == 0)
        {
            TaskDialog.Show("Column Reinforcement", "No structural columns selected.");
            return Result.Cancelled;
        }

        // Snapshot each column's Mark + geometry once, ahead of opening the dialog.
        // The dialog uses this in CSV-mode to build the validation table; in
        // single-config mode it's just used for the header count.
        var infos = BuildColumnInfos(doc, columnIds);

        string? folder  = FolderStorage.Get(doc);
        string? csvPath = FolderStorage.GetCsvPath(doc);
        var dialog = new ColumnReinforcementDialog(folder, csvPath, infos);
        if (dialog.ShowDialog() != true) return Result.Cancelled;

        // Persist any new folder / CSV path choice on the project.
        PersistPaths(doc, folder, csvPath, dialog);

        // Build the per-column mapping for the engine, with pre-skipped columns
        // tracked separately so they can be reported in the run summary.
        IDictionary<ElementId, ColumnReinforcementConfig> mapping;
        List<ColumnOutcome> preSkipped = new();

        if (dialog.SelectedMode == RunMode.Same)
        {
            ColumnReinforcementConfig cfg = dialog.Config!;
            mapping = columnIds.ToDictionary<ElementId, ElementId, ColumnReinforcementConfig>(id => id, _ => cfg);
        }
        else
        {
            mapping = BuildMappingFromCsv(infos, dialog, preSkipped);
        }

        using var group = new TransactionGroup(doc, "Column Reinforcement");
        group.Start();

        // Reset shape-pin diagnostics from any earlier run in the same session.
        _ = RebarFactory.DrainShapePinFailures();

        var reinforcer = new ColumnReinforcer(doc);
        RunResult result = reinforcer.Run(mapping, dialog.DryRun);

        // Append pre-skipped columns to the result so they show up in the dialog.
        foreach (var outcome in preSkipped)
            result.Outcomes.Add(outcome);

        if (dialog.DryRun) group.RollBack();
        else               group.Assimilate();

        ResultsDialog.Show(result);

        // If any RebarShape pin (e.g. Cranked → shape "19") was rejected by Revit,
        // surface it in a SEPARATE dialog so the user can see WHY the bar ended
        // up in the wrong (auto-matched) shape — message is the actual Revit API
        // exception, which usually points at a parametric mismatch with the
        // family (slope/length out of range, etc.).
        var pinFailures = RebarFactory.DrainShapePinFailures();
        if (pinFailures.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{pinFailures.Count} bar(s) couldn't be created in the requested RebarShape; they fell back to Revit's auto-match (which may pick the wrong family).");
            sb.AppendLine();
            // Group identical messages so a project-wide failure shows once with a count.
            var grouped = pinFailures
                .GroupBy(f => $"{f.ShapeName}|{f.BarTypeName}|{f.ExceptionType}|{f.Message}")
                .Select(g => (count: g.Count(), sample: g.First(), hostIds: g.Select(f => f.HostId).Distinct().Take(5).ToArray()))
                .OrderByDescending(t => t.count)
                .Take(8);
            foreach (var (count, sample, hostIds) in grouped)
            {
                sb.AppendLine($"× {count} bar(s) — shape '{sample.ShapeName}' / bar '{sample.BarTypeName}'");
                sb.AppendLine($"   {sample.ExceptionType}: {sample.Message}");
                sb.AppendLine($"   First hosts: {string.Join(", ", hostIds)}");
                sb.AppendLine();
            }
            var td = new TaskDialog("Column Reinforcement — shape pinning")
            {
                MainInstruction = "Shape pin fallbacks",
                MainContent     = sb.ToString(),
                CommonButtons   = TaskDialogCommonButtons.Close,
            };
            td.Show();
        }

        return Result.Succeeded;
    }

    private static IDictionary<ElementId, ColumnReinforcementConfig> BuildMappingFromCsv(
        IList<ColumnInfo> infos,
        ColumnReinforcementDialog dialog,
        List<ColumnOutcome> preSkipped)
    {
        var mapping = new Dictionary<ElementId, ColumnReinforcementConfig>();
        AssignmentTable table = dialog.Assignments!;
        ColumnReinforcementConfig? fallback = dialog.FallbackToJsonForUnassigned ? dialog.Config : null;

        foreach (var info in infos)
        {
            ColumnReinforcementConfig? cfg = table.TryGetConfig(info.Mark);
            if (cfg is not null)
            {
                mapping[info.Id] = cfg;
                continue;
            }

            if (fallback is not null)
            {
                mapping[info.Id] = fallback;
                continue;
            }

            preSkipped.Add(new ColumnOutcome
            {
                ColumnId = info.Id,
                Status   = ColumnStatus.Skipped,
                Reason   = string.IsNullOrWhiteSpace(info.Mark)
                    ? "No Mark parameter; CSV mode cannot match it. Set Mark, or use 'Same for all', or tick the fallback box."
                    : $"Mark '{info.Mark}' is not present in the CSV. Add a row, or tick the fallback box to use the selected JSON config.",
                Created  = 0,
                Replaced = 0,
            });
        }

        return mapping;
    }

    private static IList<ColumnInfo> BuildColumnInfos(Document doc, IList<ElementId> ids)
    {
        var list = new List<ColumnInfo>(ids.Count);
        foreach (ElementId id in ids)
        {
            string? mark = null;
            ColumnSection section = ColumnSection.Rectangular;
            double widthIn  = 0;
            double depthIn  = 0;

            if (doc.GetElement(id) is FamilyInstance fi)
            {
                Parameter? p = fi.LookupParameter("Mark");
                mark = p?.AsString();

                // Read geometry — but tolerate failures (e.g. slanted columns,
                // analytical-only instances) so the validation table can still
                // show the row with a blank size.
                try
                {
                    var geom = ColumnGeometry.For(fi);
                    section = geom.Section;
                    widthIn = Engine.UnitConv.FtToIn(geom.Width);
                    depthIn = Engine.UnitConv.FtToIn(geom.Depth);
                }
                catch { /* leave defaults */ }
            }

            list.Add(new ColumnInfo(id, mark, section, widthIn, depthIn));
        }
        return list;
    }

    private static void PersistPaths(
        Document doc, string? oldFolder, string? oldCsvPath, ColumnReinforcementDialog dialog)
    {
        bool folderChanged = !string.IsNullOrEmpty(dialog.FolderPath) && dialog.FolderPath != oldFolder;
        bool csvChanged    = !string.IsNullOrEmpty(dialog.CsvPath)    && dialog.CsvPath    != oldCsvPath;
        if (!folderChanged && !csvChanged) return;

        using var tx = new Transaction(doc, "Save Column Reinforcement settings");
        tx.Start();
        if (folderChanged) FolderStorage.Set       (doc, dialog.FolderPath!);
        if (csvChanged)    FolderStorage.SetCsvPath(doc, dialog.CsvPath!);
        tx.Commit();
    }

    private static IList<ElementId> GetSelectedColumnIds(UIDocument uidoc)
    {
        var filter = new ColumnSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Where(id => filter.AllowElement(uidoc.Document.GetElement(id)))
            .ToList();
    }
}
