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

    /// <summary>Reinforce every wall in <paramref name="wallIds"/> with the same <paramref name="cfg"/>.</summary>
    public RunResult Run(IEnumerable<ElementId> wallIds, ReinforcementConfig cfg, bool dryRun)
        => RunCore(wallIds, _ => cfg, dryRun);

    /// <summary>
    /// Reinforce each wall with its own config, e.g. resolved per-wall from a JSON brief
    /// (<see cref="Config.WallBrief"/> via <see cref="Config.BriefMapper"/>). Walls not present in
    /// the map are skipped.
    /// </summary>
    public RunResult Run(IReadOnlyDictionary<ElementId, ReinforcementConfig> perWall, bool dryRun)
        => RunCore(perWall.Keys, id => perWall[id], dryRun);

    private RunResult RunCore(IEnumerable<ElementId> wallIds, Func<ElementId, ReinforcementConfig> cfgFor, bool dryRun)
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

            ReinforcementConfig cfg = cfgFor(id);

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
                {
                    tx.RollBack();
                }
                else
                {
                    // A failed regeneration makes the WarningSwallower roll the transaction back;
                    // Commit() then returns RolledBack rather than throwing — surface it as a failure
                    // instead of silently reporting Success on a wall that placed nothing.
                    TransactionStatus status = tx.Commit();
                    if (status == TransactionStatus.RolledBack)
                    {
                        result.Outcomes.Add(new WallOutcome
                        {
                            WallId   = id,
                            WallName = wall.Name,
                            Status   = WallStatus.Failed,
                            Reason   = "Rolled back during regeneration (rebar geometry rejected — see warnings).",
                        });
                        continue;
                    }
                }

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
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
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
        IReadOnlyList<OpeningRect> openings = WallGeometry.GetOpenings(axes);
        WallLayering lay = WallLayering.For(_doc, axes, cfg);
        ISet<long> mergeIds = ComputeMergeOpenings(axes, cfg, openings);
        WallReinforcement.Geometry.ElevationProfile? profile = WallProfile.For(axes, _doc);

        int created = 0;
        // Field bars skip the L-corner column zone, split around openings, and clip to a non-
        // rectangular outline (slanted end/top).
        created += new FaceBarBuilder(_doc).Build(axes, cfg, lay, junctions, openings, mergeIds, profile, tag);
        created += new OpeningTrimBuilder(_doc).Build(axes, cfg, tag);
        created += new OpeningEdgeBarBuilder(_doc).Build(axes, cfg, lay, mergeIds, tag);
        // Corner / T continuity is the extended end U-bar (пэшка) — see EdgeBarBuilder.BuildEnds.
        created += new EdgeBarBuilder(_doc).Build(axes, cfg, lay, junctions, openings, mergeIds, tag);
        // The four vertical bars of the L-corner "column" inside the пэшка loop.
        created += new CornerColumnBuilder(_doc).Build(axes, cfg, junctions, tag);
        // One closed stirrup over each short opening-top strip (replaces the merged U-bars + vertical).
        created += new OpeningTopStirrupBuilder(_doc).Build(axes, cfg, lay, mergeIds, tag);
        created += new TransverseTieBuilder(_doc).Build(axes, cfg, lay, openings, tag);
        return created;
    }

    /// <summary>Opening InsertIds whose strip-above is short enough to merge the opening-top and
    /// wall-top U-bars into one closed stirrup (needs the merge option AND the top edge enabled).</summary>
    private static ISet<long> ComputeMergeOpenings(WallAxes axes, ReinforcementConfig cfg, IReadOnlyList<OpeningRect> openings)
    {
        var ids = new HashSet<long>();
        if (!cfg.Openings.MergeTopStirrup || !cfg.Edges.Top.Enabled) return ids;
        double topCover = cfg.Ft(cfg.Cover.Top);
        double legUp    = cfg.DevLengthFeet(cfg.Openings.BarType, cfg.Ft(cfg.Openings.Extension));
        double legDown  = cfg.DevLengthFeet(cfg.Edges.Top.BarType, cfg.Ft(cfg.Edges.Top.LegLength));
        foreach (OpeningRect o in openings)
            if (WallReinforcement.Geometry.OpeningTopMerge.Fires((axes.Height - topCover) - o.VMax, legUp, legDown))
                ids.Add(o.InsertId.Value);
        return ids;
    }
}
