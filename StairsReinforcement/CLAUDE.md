# StairsReinforcement — Revit 2025

## Goal
Reinforce monolithic RC stairs with a 3-step pipeline on the shared **Smart Tools** tab,
panel **Stairs Reinforcement**:

1. **Export Stairs** — write a JSON description of the selected stairs (per-flight + per-landing
   geometry, supports, available bar/hook types, hints) for the AI config agent.
2. *(agent)* — turns the JSON + the project's reinforcement brief into a sparse
   per-stair **assignments CSV** (or a single JSON config).
3. **Generate Stair Rebar** — places every bar set from the config; re-running replaces its prior
   result (idempotent by `Comments` tag).

A future **Stair Views** button (Phase 4) adds section/plan views, schedules and sheets.

## Key decisions
- **Self-contained plugin**, sibling of SlabReinforcement/ColumnReinforcement. Logic and skeleton
  are ported from SlabReinforcement (most developed); it does **not** reference those projects.
- **Two stair representations**, one domain model: native Revit `Stairs`
  (runs/landings) **and** floor-modelled flights/landings (sloped structural `Floor`s + landing
  `Floor`s). Rebar host = the `Stairs` element if it is a valid rebar host, else the `Floor`
  (resolved per component; reported when neither hosts rebar).
- **Every bar set is independently configurable** via the reusable `BarSetSpec` (bar type,
  spacing-or-count, side-cover override, per-end anchorage/hook, enable). Sets: flight bottom/top
  main + distribution, landing bottom/top X/Y mats, knee/transition bars, starter dowels, step bars.
- **ACI 318-19, Imperial (inches), non-seismic** unless told otherwise. The plugin builds geometry;
  the agent (with the engineer) decides bar sizes and spacing — it does not size steel from loads.
- **Strict** `RebarBarType`/`RebarHookType` lookup by exact name (no auto-create); throws listing
  what's available.
- **Idempotency by tag**: every placed element's `Comments` = `STR:{config}:{stairId}:{layer}`;
  a re-run deletes that prefix first (see `ExistingRebarCleaner`).
- **Sloped bars**: a flight bar's `CreateFromCurves` normal must be world-projected ⟂ its bend
  plane (`Z × runDirHoriz`), never `BasisZ` — or Revit throws an internal error.

## Docs
- `stairs-reinforcement-spec.md` — *what* (domain model, JSON schema, CSV, engine, edge cases).
- `stairs-reinforcement-dev-plan.md` — *order* (phased PR roadmap; 200–500 diff lines each).
- Agent contract trio: `stairs-dump-schema.md`, `agent-config-guide.md`, `stairs-assignments-csv-guide.md`.

## Reuse (from the monorepo)
`Length`, `ConfigLoader`, `FolderStorage` (fresh GUID), `RebarFactory`, `ExistingRebarCleaner`,
`AssignmentCsv`/`AssignmentTable`, the command/dialog/`RunResult` skeleton and `SlabSelectionFilter`
are ports from **SlabReinforcement** with namespace/GUID/category edits. The slope-frame geometry,
the dual-source stair adapter, knee/starter/step builders and the dump schema are new.
