using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StairsReinforcement.Domain;
using StairsReinforcement.Engine;

namespace StairsReinforcement.Commands;

/// <summary>
/// Exports a JSON geometry description of the selected stairs for the reinforcement agent.
/// (Phase 1 in progress — PR-02 resolves the stair model and reports the extracted geometry;
/// the JSON writer and save dialog land in PR-04.)
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
        TaskDialog.Show("Export Stairs — geometry", Summarize(assemblies));
        return Result.Succeeded;
    }

    private static string Summarize(IReadOnlyList<StairAssembly> assemblies)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Resolved {assemblies.Count} stair assembly(ies).");
        sb.AppendLine("(JSON export lands in PR-04; this confirms geometry extraction.)");

        foreach (StairAssembly a in assemblies)
        {
            sb.AppendLine();
            sb.AppendLine($"■ {a.Source} · Mark '{a.Mark ?? "—"}' · " +
                          $"{a.Flights.Count} flight(s), {a.Landings.Count} landing(s)");

            foreach (FlightComponent f in a.Flights)
                sb.AppendLine(
                    $"   Flight {f.Index} [{f.SourceKind}] hostOk={f.RebarHostOk}: " +
                    $"waist {UnitConv.FtToIn(f.WaistFt):0.#}\", width {f.WidthFt:0.##}', " +
                    $"run {f.HorizRunFt:0.##}', rise {f.TotalRiseFt:0.##}', " +
                    $"slope {UnitConv.Deg(f.SlopeRad):0.#}°, {f.RiserCount}R/{f.TreadCount}T; " +
                    $"supports {Supp(f.LowerSupport)}→{Supp(f.UpperSupport)}");

            foreach (LandingComponent l in a.Landings)
                sb.AppendLine(
                    $"   Landing {l.Index} [{l.SourceKind}]: thick {UnitConv.FtToIn(l.ThicknessFt):0.#}\", " +
                    $"elev {l.ElevationFt:0.##}', area {l.AreaSf:0.#} sf, " +
                    $"connects [{string.Join(",", l.ConnectsFlights)}], " +
                    $"supports {string.Join("/", l.Supports.Select(s => s.Kind).DefaultIfEmpty("—"))}");

            foreach (string w in a.Warnings) sb.AppendLine($"   ! {w}");
        }

        return sb.ToString();
    }

    private static string Supp(SupportInfo? s) => s is null ? "none" : s.Kind;

    private static List<Element> GetSelected(UIDocument uidoc)
    {
        var filter = new StairsSelectionFilter();
        return uidoc.Selection.GetElementIds()
            .Select(id => uidoc.Document.GetElement(id))
            .Where(filter.AllowElement)
            .ToList();
    }
}
