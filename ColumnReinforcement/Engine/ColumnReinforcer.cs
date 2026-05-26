using Autodesk.Revit.DB;
using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;

namespace ColumnReinforcement.Engine;

/// <summary>
/// Top-level orchestrator. Takes a list of column IDs + a config and runs each
/// column in its own sub-transaction so one bad column does not poison the whole
/// batch. Caller is expected to wrap the run in an outer <see cref="TransactionGroup"/>
/// (the command does); on <paramref name="dryRun"/> the sub-tx is rolled back per column.
/// </summary>
public class ColumnReinforcer
{
    private readonly Document _doc;

    public ColumnReinforcer(Document doc) => _doc = doc;

    public RunResult Run(IEnumerable<ElementId> columnIds, ColumnReinforcementConfig cfg, bool dryRun)
    {
        var result    = new RunResult { DryRun = dryRun };
        var longBuilder = new LongitudinalBarBuilder(_doc);

        foreach (ElementId id in columnIds)
        {
            if (_doc.GetElement(id) is not FamilyInstance fi)
            {
                result.Outcomes.Add(new ColumnOutcome
                {
                    ColumnId = id, Status = ColumnStatus.Skipped, Reason = "Not a family instance",
                });
                continue;
            }

            string tag = ExistingRebarCleaner.MakeTag(cfg.Name, id);

            using var tx = new Transaction(_doc, $"Reinforce column {id.Value}");
            tx.Start();
            try
            {
                ColumnGeometry geom = ColumnGeometry.For(fi);
                int created = longBuilder.Build(geom, cfg, tag);
                // Ties (PR-05), splices (PR-09), confinement (PR-07) plug in here.

                if (dryRun) tx.RollBack();
                else        tx.Commit();

                result.Outcomes.Add(new ColumnOutcome
                {
                    ColumnId = id,
                    Status   = created > 0 ? ColumnStatus.Success : ColumnStatus.Skipped,
                    Reason   = created == 0 ? "Nothing to place (check bar types and cover)" : null,
                    Created  = created,
                });
            }
            catch (Exception ex)
            {
                tx.RollBack();
                result.Outcomes.Add(new ColumnOutcome
                {
                    ColumnId = id,
                    Status   = ColumnStatus.Failed,
                    Reason   = ex.Message,
                });
            }
        }

        return result;
    }
}
