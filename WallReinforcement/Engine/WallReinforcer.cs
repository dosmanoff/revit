using Autodesk.Revit.DB;
using WallReinforcement.Config;
using WallReinforcement.Domain;

namespace WallReinforcement.Engine;

/// <summary>
/// Top-level orchestrator: takes a list of wall IDs + a config, runs each wall in
/// its own sub-transaction so one failure does not poison the whole batch.
/// </summary>
public class WallReinforcer
{
    private readonly Document _doc;

    public WallReinforcer(Document doc) => _doc = doc;

    public RunResult Run(IEnumerable<ElementId> wallIds, ReinforcementConfig cfg, bool dryRun)
    {
        var result = new RunResult { DryRun = dryRun };

        foreach (ElementId id in wallIds)
        {
            if (_doc.GetElement(id) is not Wall wall)
            {
                result.Outcomes.Add(new WallOutcome
                {
                    WallId = id, Status = WallStatus.Skipped, Reason = "Not a wall",
                });
                continue;
            }

            using var tx = new Transaction(_doc, $"Reinforce wall {id.Value}");
            tx.Start();

            // Swallow warning dialogs so a batch run never modal-blocks on one wall.
            FailureHandlingOptions failOpts = tx.GetFailureHandlingOptions();
            failOpts.SetFailuresPreprocessor(new WarningSwallower());
            failOpts.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(failOpts);

            try
            {
                int replaced = ExistingRebarCleaner.Clean(_doc, id, cfg.Name);
                int created  = ReinforceOne(wall, cfg);

                if (dryRun)
                    tx.RollBack();
                else
                    tx.Commit();

                result.Outcomes.Add(new WallOutcome
                {
                    WallId   = id,
                    WallName = wall.Name,
                    Status   = created > 0 ? WallStatus.Success : WallStatus.Skipped,
                    Reason   = created == 0 ? "Nothing to place (check bar types and cover)" : null,
                    Created  = created,
                    Replaced = replaced,
                });
            }
            catch (Exception ex)
            {
                tx.RollBack();
                result.Outcomes.Add(new WallOutcome
                {
                    WallId   = id,
                    WallName = wall.Name,
                    Status   = WallStatus.Failed,
                    Reason   = ex.Message,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Place all configured reinforcement on a single wall, WITHOUT opening a transaction — the
    /// caller must already have one open. Used by <see cref="Run"/> (inside its per-wall
    /// transaction) and by hosts that own the transaction themselves (e.g. a live-model test
    /// harness driving the engine through the MCP bridge). Does NOT clean prior rebar — call
    /// <see cref="ExistingRebarCleaner.Clean"/> first for an idempotent replace. Returns the number
    /// of bars/sets created.
    /// </summary>
    public int ReinforceOne(Wall wall, ReinforcementConfig cfg)
    {
        string tag = ExistingRebarCleaner.MakeTag(cfg.Name, wall.Id);
        WallAxes axes = WallAxes.For(wall);
        IReadOnlyList<WallJunction> junctions = WallJunctions.Detect(axes);

        int created = 0;
        created += new FaceMeshBuilder(_doc).Build(wall, cfg, tag);
        created += new OpeningTrimBuilder(_doc).Build(axes, cfg, tag);
        created += new EdgeBarBuilder(_doc).Build(axes, cfg, tag);
        created += new TransverseTieBuilder(_doc).Build(axes, cfg, tag);
        created += new CornerBarBuilder(_doc).Build(axes, cfg, junctions, tag);
        created += new TJunctionBarBuilder(_doc).Build(axes, cfg, junctions, tag);
        return created;
    }
}
