# SlabReinforcement plugin — Revit 2025

## Goal
Full slab-reinforcement pipeline for RC floors (.NET 8, C#), analogous to
`ColumnReinforcement`. Three commands on the **Smart Tools** ribbon tab, panel
`Slab Reinforcement`:
1. **Export Slabs** — dump JSON of slabs (geometry, edge adjacency, openings,
   supports-below, available bar/hook types, max bar length, lap, free-edge & zone
   hints) for an external AI agent.
2. **Generate Slab Rebar** — read `slab-assignments.csv` (produced by the agent) →
   create rebar; settable max bar length (long runs split + lapped).
3. **Slab Views** — Layer 1–4 plan views + schedules + sheets (like `ColumnViews`).

## Key decisions
- Separate new plugin; existing `SlabRebar` (rebar *classifier*) untouched.
- Field reinforcement: **two modes** — individual `Rebar` (split + lap) and
  `AreaReinforcement` — chosen via `FieldMode` in config/CSV. Bars is the default.
- Layers via independent tag in `Comments`: `SR:{config}:{slabId}:{layer}`. Views
  filter on it. **Not** coupled to the SlabRebar classifier param.
- ACI 318-19, Imperial, non-seismic, **strict** RebarBarType/RebarHookType lookup
  (repo `repo-conventions`). Plugin builds geometry; it does not size reinforcement.

## Docs
- Spec: `slab-reinforcement-spec.md` (what) — pipeline, JSON schema, CSV format,
  engine, views.
- Plan: `slab-reinforcement-dev-plan.md` (order) — phased PRs `claude/slab-rebar-NN-*`.
- Agent contract (PR-05): `slab-dump-schema.md`, `agent-config-guide.md`,
  `slab-assignments-csv-guide.md`.

## Reuse
`WallReinforcement` (RebarFactory, UnitConv, ExistingRebarCleaner, ConfigLoader,
FolderStorage, Length, EdgeBarBuilder, OpeningTrimBuilder) ·
`ColumnReinforcement` (AssignmentCsv, AssignmentTable, From-CSV dialog, RunResult) ·
`SmartViews`/`ColumnViews` (views/schedules/sheets engines).
