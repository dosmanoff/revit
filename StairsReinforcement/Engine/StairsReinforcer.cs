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

    public RunResult Run(
        IReadOnlyList<(StairAssembly Asm, StairsReinforcementConfig Cfg)> work, bool dryRun,
        IReadOnlyDictionary<long, ExpectedGeom>? expected = null)
    {
        var result = new RunResult { DryRun = dryRun };

        using var group = new TransactionGroup(_doc, "Generate Stair Rebar");
        group.Start();

        foreach ((StairAssembly asm, StairsReinforcementConfig cfg) in work)
        {
            var outcome = new StairOutcome { StairId = asm.Id.Value, Mark = cfg.Name };

            using var tx = new Transaction(_doc, $"Stair {asm.Id.Value}");
            tx.Start();
            // Non-modal failure handling: a rebar warning must never pop a blocking dialog mid-run.
            FailureHandlingOptions fo = tx.GetFailureHandlingOptions();
            fo.SetForcedModalHandling(false);
            fo.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(fo);
            try
            {
                int replaced = cfg.CleanExisting ? ExistingRebarCleaner.Clean(_doc, asm.Id, cfg.Name) : 0;
                int created = BuildAssembly(asm, cfg, outcome);

                if (expected is not null && expected.TryGetValue(asm.Id.Value, out ExpectedGeom? exp) && exp is not null)
                    Validate(asm, cfg, exp, result);

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
        var steps = new StepBarBuilder(_doc);
        var dowels = new ConnectionDowelBuilder(_doc);

        foreach (FlightComponent f in asm.Flights)
        {
            if (!f.RebarHostOk)
            {
                outcome.Reason = Append(outcome.Reason, $"flight {f.Index}: host cannot hold rebar");
                continue;
            }
            LandingComponent? lowerLanding = FindLanding(asm, f.LowerSupport);
            LandingComponent? upperLanding = FindLanding(asm, f.UpperSupport);
            created += Safe(() => longitudinal.Build(f, cfg, asm.Id, lowerLanding, upperLanding), $"flight {f.Index} long", outcome);
            created += Safe(() => distribution.Build(f, cfg, asm.Id), $"flight {f.Index} dist", outcome);
            created += Safe(() => steps.Build(f, cfg, asm.Id), $"flight {f.Index} steps", outcome);
            created += Safe(() => dowels.Build(f, cfg, asm.Id), $"flight {f.Index} dowels", outcome);
        }

        var landingMat = new LandingMatBuilder(_doc);
        var landingEdge = new LandingEdgeBuilder(_doc);
        foreach (LandingComponent l in asm.Landings)
        {
            if (!l.RebarHostOk)
            {
                outcome.Reason = Append(outcome.Reason, $"landing {l.Index}: host cannot hold rebar");
                continue;
            }
            created += Safe(() => landingMat.Build(l, cfg, asm.Id), $"landing {l.Index} mat", outcome);
            created += Safe(() => landingEdge.Build(l, cfg, asm.Id), $"landing {l.Index} пэшки", outcome);
        }

        // Bottom continuity into landings is carried by the flight main bars themselves (above), so the
        // separate knee bar is not placed — it duplicated those bars and tangled the junction.
        created += Safe(() => new StarterBarBuilder(_doc).Build(asm, cfg, asm.Id), "starters", outcome);

        return created;
    }

    private static LandingComponent? FindLanding(StairAssembly asm, SupportInfo? support) =>
        support?.Kind == "landing"
            ? asm.Landings.FirstOrDefault(l => l.ComponentId.Value == support.ElementId)
            : null;

    /// <summary>Run one bar-set builder; isolate a failure to that set (record it) instead of failing the whole stair.</summary>
    private static int Safe(Func<int> build, string label, StairOutcome outcome)
    {
        try { return build(); }
        catch (Exception ex) { outcome.Reason = Append(outcome.Reason, $"{label}: {ex.Message}"); return 0; }
    }

    private static string Append(string? reason, string add) =>
        string.IsNullOrEmpty(reason) ? add : $"{reason}; {add}";

    /// <summary>Warn (non-fatal) when the live geometry differs from the CSV Expected* values.</summary>
    private static void Validate(StairAssembly asm, StairsReinforcementConfig cfg, ExpectedGeom exp, RunResult result)
    {
        FlightComponent? f = asm.Flights.FirstOrDefault();
        if (f is null) return;
        const double tolFt = 0.5 / 12.0;   // ~1/2 inch

        Check(result, asm, cfg, "waist", exp.Waist, f.WaistFt, tolFt);
        Check(result, asm, cfg, "width", exp.Width, f.WidthFt, tolFt);
        Check(result, asm, cfg, "rise", exp.Rise, f.TotalRiseFt, tolFt);
    }

    private static void Check(RunResult result, StairAssembly asm, StairsReinforcementConfig cfg,
        string what, Config.Length? expected, double liveFt, double tolFt)
    {
        if (expected is not { } e) return;
        double expFt = cfg.Ft(e);
        if (Math.Abs(expFt - liveFt) > tolFt)
            result.Warnings.Add(
                $"{cfg.Name} (stair {asm.Id.Value}): expected {what} {expFt * 12:0.#}\" but model is {liveFt * 12:0.#}\".");
    }
}
