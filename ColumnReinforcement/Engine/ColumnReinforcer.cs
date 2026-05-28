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

    /// <summary>
    /// Single-config overload — every column in <paramref name="columnIds"/> gets
    /// the same <paramref name="cfg"/>. Internally builds a uniform mapping and
    /// delegates to the per-column overload.
    /// </summary>
    public RunResult Run(IEnumerable<ElementId> columnIds, ColumnReinforcementConfig cfg, bool dryRun)
    {
        var mapping = columnIds.ToDictionary<ElementId, ElementId, ColumnReinforcementConfig>(
            id => id, _ => cfg);
        return Run(mapping, dryRun);
    }

    /// <summary>
    /// Per-column overload. <paramref name="perColumn"/> maps each column's
    /// <see cref="ElementId"/> to its own <see cref="ColumnReinforcementConfig"/> —
    /// supports the Phase-4 "From CSV" mode where each Mark has its own settings.
    /// </summary>
    public RunResult Run(IDictionary<ElementId, ColumnReinforcementConfig> perColumn, bool dryRun)
    {
        var result        = new RunResult { DryRun = dryRun };
        var longBuilder   = new LongitudinalBarBuilder(_doc);
        var tieBuilder    = new StirrupBuilder(_doc);
        var dowelBuilder  = new FoundationDowelBuilder(_doc);
        var spliceBuilder = new UpperSpliceBuilder(_doc);

        foreach ((ElementId id, ColumnReinforcementConfig cfg) in perColumn)
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

                // Resolve hosts once per column — both for dowels (below) and for the
                // longitudinal "bent into slab above" termination and the upper splices.
                (Element host, DowelHost kind)? dowelHostInfo = cfg.Dowels.Enabled
                    ? HostContext.ResolveDowelHost(fi, geom, cfg.Dowels.Host, cfg.Dowels.OnlyStructuralFoundation)
                    : null;

                // BentToSlab bars (default or any per-bar override) need the slab above,
                // as do upper splices.
                bool longWantsSlab =
                    cfg.Longitudinal.TopDefault == BarTopMode.BentToSlab ||
                    (cfg.Longitudinal.TopModes?.IndexOf("B", StringComparison.OrdinalIgnoreCase) >= 0);
                bool needsSlabAbove = cfg.UpperSplices.Enabled || longWantsSlab;
                Element? slabAbove = needsSlabAbove ? HostContext.FindSlabAbove(fi, geom) : null;

                int created = 0;
                created += longBuilder.Build(geom, cfg, tag, slabAbove);
                created += tieBuilder.Build(geom, cfg, tag);
                FoundationDowelBuilder.Result dowelResult  = dowelBuilder.Build(geom, cfg, tag, dowelHostInfo);
                UpperSpliceBuilder.Result     spliceResult = spliceBuilder.Build(geom, cfg, tag, slabAbove);
                created += dowelResult.Created;
                created += spliceResult.Created;
                // Inner ties (Phase 3) plug in here.

                if (dryRun) tx.RollBack();
                else        tx.Commit();

                string? reason = JoinReasons(dowelResult.SkipReason, spliceResult.SkipReason)
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

    private static string? JoinReasons(params string?[] reasons)
    {
        var nonEmpty = reasons.Where(r => !string.IsNullOrEmpty(r)).ToList();
        return nonEmpty.Count == 0 ? null : string.Join(" | ", nonEmpty);
    }
}
