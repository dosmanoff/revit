using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SlabReinforcement.Domain;
using SlabReinforcement.Engine;

namespace SlabReinforcement.Commands;

/// <summary>
/// Stage 1 of the pipeline: dump a JSON description of the selected slabs for the
/// external reinforcement agent. PR-02 interim: summarizes the extracted geometry
/// (thickness, local basis, area, openings) so SlabGeometry can be smoke-tested.
/// Full JSON export lands in PR-04.
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
                    "Select floor slabs to summarize, then click Finish");
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

        var sb = new StringBuilder();
        foreach (Floor floor in floors)
        {
            string label = Describe(floor);
            try
            {
                SlabGeometry g = SlabGeometry.For(floor);
                SlabContext ctx = SlabContext.For(g);
                IReadOnlyList<SlabOpening> ops = SlabOpenings.For(g);

                int free = ctx.Edges.Count(e => e.Kind == EdgeKind.Free);
                int wall = ctx.Edges.Count(e => e.Kind == EdgeKind.Wall);
                int beam = ctx.Edges.Count(e => e.Kind == EdgeKind.Beam);
                int slab = ctx.Edges.Count(e => e.Kind == EdgeKind.Slab);

                sb.AppendLine($"{label}:");
                sb.AppendLine(
                    $"   t = {UnitConv.FtToIn(g.ThicknessFt):0.#}\"   basisX = {g.XWorldDeg:0.#}°   area = {g.NetAreaSf:0.#} sf");
                sb.AppendLine(
                    $"   edges: free={free} wall={wall} beam={beam} slab={slab} (of {ctx.Edges.Count})");
                sb.AppendLine(
                    $"   supports below = {ctx.Supports.Count}   openings = {ops.Count} (trim {SlabOpenings.TrimCount(ops)})");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{label}:  ERROR — {ex.Message}");
            }
        }

        TaskDialog.Show("Export Slabs — geometry (PR-02 interim)", sb.ToString());
        return Result.Succeeded;
    }

    private static List<Floor> GetSelectedFloors(UIDocument uidoc)
    {
        Document doc = uidoc.Document;
        return uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .OfType<Floor>()
            .ToList();
    }

    private static string Describe(Floor floor)
    {
        string? mark = floor.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        string id = $"Floor {floor.Id.Value}";
        return string.IsNullOrWhiteSpace(mark) ? id : $"{mark} ({id})";
    }
}
