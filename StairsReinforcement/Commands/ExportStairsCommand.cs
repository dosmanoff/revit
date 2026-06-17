using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StairsReinforcement.Domain;
using StairsReinforcement.Export;

namespace StairsReinforcement.Commands;

/// <summary>
/// Exports a JSON geometry description of the selected stairs (native Stairs and/or
/// floor-modelled) for the reinforcement agent: per-flight + per-landing geometry, supports,
/// available bar/hook types and document resources.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ExportStairsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        List<Element> picked = GetSelected(uidoc);
        if (picked.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element, new StairsSelectionFilter(),
                    "Select stairs (or floor-modelled flights/landings) to export, then click Finish");
                picked = refs.Select(r => doc.GetElement(r.ElementId)).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }

        if (picked.Count == 0)
        {
            TaskDialog.Show("Export Stairs", "No stairs or structural floors selected.");
            return Result.Cancelled;
        }

        List<StairAssembly> assemblies = StairSourceResolver.Resolve(doc, picked);
        StairsDump dump = new StairsDumpBuilder(doc).Build(assemblies, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));

        string? path = AskSavePath(doc);
        if (path is null) return Result.Cancelled;

        try { File.WriteAllText(path, dump.ToJson(), new UTF8Encoding(false)); }
        catch (Exception ex) { message = $"Could not write '{path}': {ex.Message}"; return Result.Failed; }

        TaskDialog.Show("Export Stairs", Summarize(dump, path));
        return Result.Succeeded;
    }

    private static string? AskSavePath(Document doc)
    {
        string title = string.IsNullOrWhiteSpace(doc.Title) ? "stairs" : Path.GetFileNameWithoutExtension(doc.Title);
        string initialDir = !string.IsNullOrWhiteSpace(doc.PathName)
            ? Path.GetDirectoryName(doc.PathName)!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export stairs JSON",
            Filter = "JSON (*.json)|*.json",
            FileName = $"{title}_stairs.json",
            InitialDirectory = initialDir,
            OverwritePrompt = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static string Summarize(StairsDump dump, string path)
    {
        int flights = dump.Stairs.Sum(s => s.Flights.Count);
        int landings = dump.Stairs.Sum(s => s.Landings.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {dump.Stairs.Count} stair(s): {flights} flight(s), {landings} landing(s).");
        sb.AppendLine($"{dump.AvailableRebarBarTypes.Count} bar types, {dump.AvailableRebarHookTypes.Count} hook types.");
        sb.AppendLine(path);

        var allWarnings = dump.Warnings
            .Concat(dump.Stairs.SelectMany(s => s.Warnings ?? new List<string>()))
            .ToList();
        if (allWarnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Warnings:");
            foreach (string w in allWarnings.Take(5)) sb.AppendLine($"  • {w}");
            if (allWarnings.Count > 5) sb.AppendLine($"  … and {allWarnings.Count - 5} more");
        }
        return sb.ToString();
    }

    private static List<Element> GetSelected(UIDocument uidoc)
    {
        var filter = new StairsSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Select(id => uidoc.Document.GetElement(id))
            .Where(filter.AllowElement)
            .ToList();
    }
}
