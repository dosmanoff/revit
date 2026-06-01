using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SlabReinforcement.Export;

namespace SlabReinforcement.Commands;

/// <summary>
/// Stage 1 of the pipeline: dump a JSON description of the selected slabs for the external
/// reinforcement agent (geometry, edge adjacency, openings, supports below, available
/// bar/hook types, hints). Schema documented in slab-dump-schema.md.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ExportSlabsCommand : IExternalCommand
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
                    "Select floor slabs to export, then click Finish");
                floors = refs.Select(r => doc.GetElement(r.ElementId)).OfType<Floor>().ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }

        if (floors.Count == 0)
        {
            TaskDialog.Show("Export Slabs", "No floor slabs selected.");
            return Result.Cancelled;
        }

        SlabDump dump = new SlabDumpBuilder(doc).Build(floors, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));

        string? path = AskSavePath(doc);
        if (path is null) return Result.Cancelled;

        try
        {
            File.WriteAllText(path, dump.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            message = $"Could not write '{path}': {ex.Message}";
            return Result.Failed;
        }

        ShowSummary(dump, path);
        return Result.Succeeded;
    }

    private static string? AskSavePath(Document doc)
    {
        string initialDir = !string.IsNullOrWhiteSpace(doc.PathName)
            ? Path.GetDirectoryName(doc.PathName)!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        string title = string.IsNullOrWhiteSpace(doc.Title) ? "model" : Path.GetFileNameWithoutExtension(doc.Title);

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export slabs JSON",
            Filter = "JSON (*.json)|*.json",
            FileName = $"{title}_slabs.json",
            InitialDirectory = initialDir,
            OverwritePrompt = true,
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static void ShowSummary(SlabDump dump, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Exported {dump.Slabs.Count} slab(s) to:");
        sb.AppendLine(path);
        sb.AppendLine();
        sb.AppendLine($"Bar types: {dump.AvailableRebarBarTypes.Count}   hook types: {dump.AvailableRebarHookTypes.Count}");

        if (dump.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Warnings ({dump.Warnings.Count}):");
            foreach (string w in dump.Warnings.Take(5)) sb.AppendLine($"  • {w}");
            if (dump.Warnings.Count > 5) sb.AppendLine($"  … and {dump.Warnings.Count - 5} more.");
        }

        TaskDialog.Show("Export Slabs", sb.ToString());
    }

    private static List<Floor> GetSelectedFloors(UIDocument uidoc)
    {
        Document doc = uidoc.Document;
        return uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .OfType<Floor>()
            .ToList();
    }
}
