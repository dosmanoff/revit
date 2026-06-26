using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WallReinforcement.Export;

namespace WallReinforcement.Commands;

/// <summary>
/// Stage 1 of the agent pipeline: dump a JSON description of the selected walls for the external
/// reinforcement agent (geometry, openings, L-corner / T-stem junctions, cover, available
/// bar/hook types, hints). Schema documented in wall-dump-schema.md. The agent turns it into a
/// wall brief that <see cref="WallReinforcementCommand"/> consumes.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ExportWallsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        List<Wall> walls = GetSelectedWalls(uidoc);
        if (walls.Count == 0)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element, new WallSelectionFilter(),
                    "Select walls to export, then click Finish");
                walls = refs.Select(r => doc.GetElement(r.ElementId)).OfType<Wall>().ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }

        if (walls.Count == 0)
        {
            TaskDialog.Show("Export Walls", "No walls selected.");
            return Result.Cancelled;
        }

        WallDump dump = new WallDumpBuilder(doc).Build(walls, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));

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
            Title = "Export walls JSON",
            Filter = "JSON (*.json)|*.json",
            FileName = $"{title}_walls.json",
            InitialDirectory = initialDir,
            OverwritePrompt = true,
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static void ShowSummary(WallDump dump, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Exported {dump.Walls.Count} wall(s) to:");
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

        TaskDialog.Show("Export Walls", sb.ToString());
    }

    private static List<Wall> GetSelectedWalls(UIDocument uidoc)
    {
        Document doc = uidoc.Document;
        var filter = new WallSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .OfType<Wall>()
            .Where(w => filter.AllowElement(w))
            .ToList();
    }
}
