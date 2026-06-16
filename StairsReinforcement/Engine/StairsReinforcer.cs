using Autodesk.Revit.DB;
using StairsReinforcement.Config;
using StairsReinforcement.Domain;

namespace StairsReinforcement.Engine;

/// <summary>
/// Orchestrates rebar generation: one <see cref="TransactionGroup"/> per run, one
/// <see cref="Transaction"/> per stair (clean prior STR-tagged bars → build sets → commit/rollback).
/// A dry run rolls everything back. A component whose host can't hold rebar is skipped with a reason
/// (so a native-Stairs hosting limitation never aborts the batch).
/// </summary>
public sealed class StairsReinforcer
{
    private readonly Document _doc;
    public StairsReinforcer(Document doc) => _doc = doc;

    public RunResult Run(IReadOnlyList<(StairAssembly Asm, StairsReinforcementConfig Cfg)> work, bool dryRun)
    {
        var result = new RunResult { DryRun = dryRun };

        using var group = new TransactionGroup(_doc, "Generate Stair Rebar");
        group.Start();

        foreach ((StairAssembly asm, StairsReinforcementConfig cfg) in work)
        {
            var outcome = new StairOutcome { StairId = asm.Id.Value, Mark = cfg.Name };

            using var tx = new Transaction(_doc, $"Stair {asm.Id.Value}");
            tx.Start();
            try
            {
                int replaced = cfg.CleanExisting ? ExistingRebarCleaner.Clean(_doc, asm.Id, cfg.Name) : 0;
                int created = BuildAssembly(asm, cfg, outcome);

                outcome.Created = created;
                outcome.Replaced = replaced;
                outcome.Status = created > 0 ? StairStatus.Success : StairStatus.Skipped;
                if (created == 0 && outcome.Reason is null)
                    outcome.Reason = "nothing placed (check bar types, geometry and rebar host).";

                if (dryRun) tx.RollBack(); else tx.Commit();
            }
            catch (Exception ex)
            {
                tx.RollBack();
                outcome.Status = StairStatus.Failed;
                outcome.Reason = ex.Message;
            }

            result.Outcomes.Add(outcome);
        }

        if (dryRun) group.RollBack(); else group.Assimilate();
        return result;
    }

    private int BuildAssembly(StairAssembly asm, StairsReinforcementConfig cfg, StairOutcome outcome)
    {
        int created = 0;
        var longitudinal = new FlightLongitudinalBuilder(_doc);
        var distribution = new FlightDistributionBuilder(_doc);

        foreach (FlightComponent f in asm.Flights)
        {
            if (!f.RebarHostOk)
            {
                outcome.Reason = Append(outcome.Reason, $"flight {f.Index}: host cannot hold rebar");
                continue;
            }
            created += longitudinal.Build(f, cfg, asm.Id);
            created += distribution.Build(f, cfg, asm.Id);
        }

        var landingMat = new LandingMatBuilder(_doc);
        foreach (LandingComponent l in asm.Landings)
        {
            if (!l.RebarHostOk)
            {
                outcome.Reason = Append(outcome.Reason, $"landing {l.Index}: host cannot hold rebar");
                continue;
            }
            created += landingMat.Build(l, cfg, asm.Id);
        }

        created += new KneeBarBuilder(_doc).Build(asm, cfg, asm.Id);

        // Starters, steps are wired in PR-11…PR-12.
        return created;
    }

    private static string Append(string? reason, string add) =>
        string.IsNullOrEmpty(reason) ? add : $"{reason}; {add}";
}
