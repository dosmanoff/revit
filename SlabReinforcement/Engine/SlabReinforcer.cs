using Autodesk.Revit.DB;
using SlabReinforcement.Config;
using SlabReinforcement.Domain;

namespace SlabReinforcement.Engine;

/// <summary>
/// Orchestrates rebar generation: one <see cref="TransactionGroup"/> for the run, one
/// <see cref="Transaction"/> per slab (clean prior SR: rebar → build → commit/rollback).
/// A dry run rolls everything back so nothing is left in the model.
/// </summary>
public sealed class SlabReinforcer
{
    private readonly Document _doc;

    public SlabReinforcer(Document doc) => _doc = doc;

    public RunResult Run(
        IDictionary<ElementId, SlabReinforcementConfig> perSlab,
        IReadOnlyList<ZoneSpec> zones, bool dryRun)
    {
        var result = new RunResult { DryRun = dryRun };

        using var group = new TransactionGroup(_doc, "Generate Slab Rebar");
        group.Start();

        foreach ((ElementId slabId, SlabReinforcementConfig cfg) in perSlab)
        {
            var outcome = new SlabOutcome { SlabId = slabId.Value, Mark = cfg.Name };

            if (_doc.GetElement(slabId) is not Floor floor)
            {
                outcome.Status = SlabStatus.Skipped;
                outcome.Reason = "Not a floor.";
                result.Outcomes.Add(outcome);
                continue;
            }

            using var tx = new Transaction(_doc, $"Slab {slabId.Value}");
            tx.Start();
            try
            {
                int replaced = cfg.CleanExisting ? ExistingRebarCleaner.Clean(_doc, slabId, cfg.Name) : 0;
                SlabGeometry geom = SlabGeometry.For(floor);
                SlabContext ctx = SlabContext.For(geom);

                int created = cfg.FieldMode switch
                {
                    FieldMode.Bars => new FieldBarBuilder(_doc).Build(geom, cfg, slabId),
                    FieldMode.Sets => new FieldSetBuilder(_doc).Build(geom, cfg, slabId),
                    _ => new FieldMeshBuilder(_doc).Build(geom, cfg, slabId),
                };

                if (cfg.Edges.UBarsEnabled)
                    created += new EdgeUBarBuilder(_doc).Build(geom, cfg, slabId, ctx);

                if (cfg.Openings.TrimEnabled)
                    created += new OpeningTrimBuilder(_doc).Build(geom, cfg, slabId);

                string? mark = floor.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                List<ZoneSpec> slabZones = zones
                    .Where(zn => string.Equals(zn.SlabMark, mark, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (slabZones.Count > 0)
                    created += new SupportZoneBuilder(_doc).Build(geom, cfg, slabId, ctx, slabZones);

                outcome.Created = created;
                outcome.Replaced = replaced;
                outcome.Status = created > 0 ? SlabStatus.Success : SlabStatus.Skipped;
                if (created == 0 && outcome.Reason is null)
                    outcome.Reason = "Nothing placed (check bar types, cover and geometry).";

                if (dryRun) tx.RollBack(); else tx.Commit();
            }
            catch (Exception ex)
            {
                tx.RollBack();
                outcome.Status = SlabStatus.Failed;
                outcome.Reason = ex.Message;
            }

            result.Outcomes.Add(outcome);
        }

        if (dryRun) group.RollBack(); else group.Assimilate();
        return result;
    }
}
