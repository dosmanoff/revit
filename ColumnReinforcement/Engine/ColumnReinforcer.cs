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
        var result        = new RunResult { DryRun = dryRun };
        var longBuilder   = new LongitudinalBarBuilder(_doc);
        var tieBuilder    = new StirrupBuilder(_doc);
        var dowelBuilder  = new FoundationDowelBuilder(_doc);

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
                int replaced = cfg.CleanExisting
                    ? ExistingRebarCleaner.Clean(_doc, id, cfg.Name)
                    : 0;

                ColumnGeometry geom = ColumnGeometry.For(fi);

                Element? slabBelow = cfg.Dowels.Enabled
                    ? HostContext.FindSlabBelow(fi, geom, cfg.Dowels.OnlyStructuralFoundation)
                    : null;

                int created = 0;
                created += longBuilder.Build(geom, cfg, tag);
                created += tieBuilder.Build(geom, cfg, tag);
                FoundationDowelBuilder.Result dowelResult = dowelBuilder.Build(geom, cfg, tag, slabBelow);
                created += dowelResult.Created;
                // Splices (PR-10), inner ties (Phase 3) plug in here.

                if (dryRun) tx.RollBack();
                else        tx.Commit();

                string? reason = dowelResult.SkipReason
                    ?? (created == 0 ? "Nothing to place (check bar types and cover)" : null);

                result.Outcomes.Add(new ColumnOutcome
                {
                    ColumnId = id,
                    Status   = created > 0 ? ColumnStatus.Success : ColumnStatus.Skipped,
                    Reason   = reason,
                    Created  = created,
                    Replaced = replaced,
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
