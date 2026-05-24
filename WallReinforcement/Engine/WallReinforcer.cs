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
        var result      = new RunResult { DryRun = dryRun };
        var meshBuilder = new FaceMeshBuilder(_doc);
        var trimBuilder = new OpeningTrimBuilder(_doc);
        var edgeBuilder = new EdgeBarBuilder(_doc);

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

            string tag = ExistingRebarCleaner.MakeTag(cfg.Name, id);

            using var tx = new Transaction(_doc, $"Reinforce wall {id.Value}");
            tx.Start();
            try
            {
                int replaced = ExistingRebarCleaner.Clean(_doc, id, cfg.Name);
                WallAxes axes = WallAxes.For(wall);

                int created = 0;
                created += meshBuilder.Build(wall, cfg, tag);
                created += trimBuilder.Build(axes, cfg, tag);
                created += edgeBuilder.Build(axes, cfg, tag);

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
}
