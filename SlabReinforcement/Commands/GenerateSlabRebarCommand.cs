using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;
using SlabReinforcement.Engine;
using SlabReinforcement.UI;

namespace SlabReinforcement.Commands;

/// <summary>
/// Stage 3: read a per-slab assignments CSV (or a single JSON config) and generate rebar on
/// the selected floors, splitting long runs at the max bar length and lapping them.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class GenerateSlabRebarCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        List<Floor> floors = GetSelectedFloors(uidoc);
        if (floors.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element, new SlabSelectionFilter(),
                    "Select floor slabs to reinforce, then click Finish");
                floors = refs.Select(r => doc.GetElement(r.ElementId)).OfType<Floor>().ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }
        if (floors.Count == 0)
        {
            TaskDialog.Show("Generate Slab Rebar", "No floor slabs selected.");
            return Result.Cancelled;
        }

        var dlg = new SlabRebarGenDialog(doc);
        if (dlg.ShowDialog() != true) return Result.Cancelled;

        PersistPaths(doc, dlg);

        var skipped = new List<string>();
        Dictionary<ElementId, SlabReinforcementConfig> perSlab;
        try
        {
            perSlab = dlg.FromCsv
                ? FromCsv(doc, floors, dlg, skipped)
                : SameForAll(floors, dlg);
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }

        if (perSlab.Count == 0)
        {
            TaskDialog.Show("Generate Slab Rebar",
                "Nothing to do — no slab matched a config.\n\n" + string.Join("\n", skipped));
            return Result.Cancelled;
        }

        IReadOnlyList<ZoneSpec> zones = LoadZones(dlg);

        RunResult result = new SlabReinforcer(doc).Run(perSlab, zones, dlg.DryRun);
        ShowResults(result, skipped);
        return Result.Succeeded;
    }

    // ── Config resolution ────────────────────────────────────────────────────────

    private static Dictionary<ElementId, SlabReinforcementConfig> SameForAll(List<Floor> floors, SlabRebarGenDialog dlg)
    {
        if (dlg.ConfigPath is null)
            throw new InvalidOperationException("No config file selected. Pick a config folder and file, or switch to From CSV.");

        SlabReinforcementConfig cfg = ConfigLoader.Load(dlg.ConfigPath);
        ApplyMaxOverride(cfg, dlg.MaxBarOverride);

        var map = new Dictionary<ElementId, SlabReinforcementConfig>();
        foreach (Floor f in floors) map[f.Id] = cfg;
        return map;
    }

    private static Dictionary<ElementId, SlabReinforcementConfig> FromCsv(
        Document doc, List<Floor> floors, SlabRebarGenDialog dlg, List<string> skipped)
    {
        if (dlg.CsvPath is null || !File.Exists(dlg.CsvPath))
            throw new InvalidOperationException("Assignments CSV not found. Browse to a valid file.");

        AssignmentTable table = AssignmentCsv.Parse(File.ReadAllText(dlg.CsvPath), dlg.CsvPath);

        var map = new Dictionary<ElementId, SlabReinforcementConfig>();
        foreach (Floor f in floors)
        {
            string? mark = MarkOf(f);
            SlabReinforcementConfig? cfg = mark is null ? null : table.TryGetConfig(mark);
            if (cfg is null)
            {
                skipped.Add($"{Describe(f)}: no CSV row for Mark '{mark ?? "(none)"}'.");
                continue;
            }
            ApplyMaxOverride(cfg, dlg.MaxBarOverride);
            map[f.Id] = cfg;
        }
        return map;
    }

    private static IReadOnlyList<ZoneSpec> LoadZones(SlabRebarGenDialog dlg)
    {
        if (dlg.ZonesPath is null || !File.Exists(dlg.ZonesPath)) return [];
        return AssignmentCsv.ParseZones(File.ReadAllText(dlg.ZonesPath), dlg.ZonesPath).Zones;
    }

    private static void ApplyMaxOverride(SlabReinforcementConfig cfg, string? maxBar)
    {
        if (maxBar is null) return;
        cfg.Lengths.MaxBarLength = double.TryParse(maxBar, NumberStyles.Any, CultureInfo.InvariantCulture, out double n)
            ? new Length(n)
            : new Length(maxBar);
    }

    private static void PersistPaths(Document doc, SlabRebarGenDialog dlg)
    {
        try
        {
            using var t = new Transaction(doc, "Slab rebar settings");
            t.Start();
            if (dlg.ConfigFolder is { } folder) FolderStorage.SetConfigFolder(doc, folder);
            if (dlg.CsvPath is { } csv) FolderStorage.SetCsvPath(doc, csv);
            if (dlg.ZonesPath is { } zones) FolderStorage.SetZonesPath(doc, zones);
            t.Commit();
        }
        catch { /* persistence is best-effort */ }
    }

    // ── Output ───────────────────────────────────────────────────────────────────

    private static void ShowResults(RunResult result, List<string> skipped)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.DryRun ? "DRY RUN — nothing was kept.\n" : "");
        sb.AppendLine($"Succeeded: {result.Succeeded}   Skipped: {result.Skipped}   Failed: {result.Failed}");
        sb.AppendLine($"Bars created: {result.TotalCreated}   replaced: {result.TotalReplaced}");
        sb.AppendLine();

        foreach (SlabOutcome o in result.Outcomes.Where(o => o.Status != SlabStatus.Success).Take(12))
            sb.AppendLine($"  {o.Mark ?? o.SlabId.ToString()}: {o.Status} — {o.Reason}");
        foreach (string s in skipped.Take(12))
            sb.AppendLine($"  {s}");

        TaskDialog.Show("Generate Slab Rebar", sb.ToString().TrimEnd());
    }

    private static List<Floor> GetSelectedFloors(UIDocument uidoc)
    {
        Document doc = uidoc.Document;
        return uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).OfType<Floor>().ToList();
    }

    private static string? MarkOf(Element e)
    {
        string? m = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        return string.IsNullOrWhiteSpace(m) ? null : m;
    }

    private static string Describe(Floor f)
    {
        string id = $"Floor {f.Id.Value}";
        string? mark = MarkOf(f);
        return mark is null ? id : $"{mark} ({id})";
    }
}
