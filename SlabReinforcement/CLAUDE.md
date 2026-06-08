# SlabReinforcement plugin — Revit 2025

## Goal
Full slab-reinforcement pipeline for RC floors (.NET 8, C#), analogous to
`ColumnReinforcement`. **Three sections** (buttons) on the **Smart Tools** ribbon tab,
panel `Slab Reinforcement`:
1. **Slab Geometry Analysis** — dump JSON of the slab + adjacent structures (face-geometry
   openings of any shape with class/reason, boundary adjacency free/beam/wall/neighbour-slab,
   supports below, slab above/below, available bar/hook types, hints) for the agent.
2. **Generate Slab Rebar** — read the agent's **JSON brief** (`SlabBrief`; legacy CSV still
   supported) → create rebar. Field as `Bars` | **`Sets`** | `AreaSystem`; max bar length
   split + lapped; per-segment edges (U-bar / 90° bend / into-support); arbitrary detailed
   groups + dowels (into wall/stair/slab-above); smart opening trim (skips shafts / edge-adjacent).
3. **Slab Views** — Layer 1–4 plans + cross-sections (each way) + 3D cage + bending details +
   schedules + sheets (≥ `ColumnViews`).

## Key decisions
- Separate new plugin; existing `SlabRebar` (rebar *classifier*) untouched.
- Field reinforcement: **three modes** — `Bars` (split+lap), **`Sets`** (one Rebar set per band,
  representative bar, laps by splitting — default for the brief), `AreaReinforcement`.
- Brief format: **structured JSON** (`SlabBrief`, see `slab-brief-schema.md`); CSV is legacy.
- Openings from the slab **face geometry** (robust to any shape/method), auto-classified
  Trim/Shaft/EdgeAdjacent so trim isn't excessive.
- Layers via independent `Comments` tag `SR:{config}:{slabId}:{layer}`. Views filter on it.
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
