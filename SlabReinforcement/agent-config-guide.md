# Agent guide — turning a slab dump into `slab-assignments.csv`

Operating manual for the **config-generation agent**: given a slab dump
(`schema_version ≥ 1`) plus the project's reinforcement brief, produce a sparse
`slab-assignments.csv` (+ `slab-zones.csv`) that **Generate Slab Rebar** loads.

A *decision procedure*, not a field reference — for field meanings/units see
[`slab-assignments-csv-guide.md`](slab-assignments-csv-guide.md); for the input see
[`slab-dump-schema.md`](slab-dump-schema.md).

US norms: **ACI 318-19, Imperial (inches), non-seismic unless told otherwise.** The
plugin builds geometry; **you** (with the brief/engineer) decide bar sizes and spacing —
the plugin does not size reinforcement from loads.

---

## 0. The flow

```
Revit ──[Export Slabs]──► slabs.json ─┐
                                       │  YOU = generation agent
   project reinforcement brief ────────┤
                                       ▼
                       slab-assignments.csv  (+ slab-zones.csv)
                                       │
                       Generate Slab Rebar → From CSV → Run
```

One row per **Mark**. Keep it sparse — emit a field only when it differs from the default.

---

## 1. Read the inputs

| Datum | Source in the dump | Use |
|---|---|---|
| **Reinforcement spec** | `comments` (instance *Comments*) | **Primary.** Free text like `B: #5@12 EW; T: #5@12 EW over cols` → bottom `#5`@12" each way, top `#5`@12" over supports. Confirm the field with the user — some projects use a custom parameter. |
| Section | `thickness_in`, `level`, `is_foundation` | Sanity / cover defaults. |
| Frame | `local_basis.x_world_deg` | X/Y in the CSV are in this frame. |
| Cover | `rebar_cover.{top,bottom}` | Use when present; else default 1.5". |
| Free edges | `hints.free_edge_indices`, `boundary[*].edge=="free"` | → edge U-bars. |
| Supports below | `context.supports_below`, `hints.supports` | → top strengthening zones (columns) in `slab-zones.csv`. |
| Openings | `openings`, `hints.openings_need_trim` | → opening trim. |
| Span / shape | `hints.max_span_ft`, `hints.is_two_way` | Bottom/top layout sanity. |
| Allow-lists | `available_rebar_bar_types`, `available_rebar_hook_types` | **Strict** — emit only these names. |

---

## 2. Field mesh (bottom + top)

From the spec, e.g. `B: #5@12 EW`:
- `BottomXBarType=#5 BottomXSpacing=12`, `BottomYBarType=#5 BottomYSpacing=12` (EW = each way).
- Bottom mesh is the baseline for every two-way slab.

Top mesh — pick `TopMode`:
- **`OverSupports`** (typical flat plate): top steel only in column strips → leave the
  field-mesh top off and put the bars in `slab-zones.csv` (§4). Most common.
- **`Continuous`**: full top mat (transfer slabs, mats) → set `TopX*/TopY*`.
- **`Edges`**: top bars only along supported edges (continuity with neighbours).
- **`None`**: bottom only.

Choose `FieldMode`:
- **`Bars`** (default) when the brief cares about **bar lengths, laps, or schedules** —
  the plugin splits long runs at `MaxBarLength` and laps them.
- **`AreaSystem`** when a native Revit rebar **system** is wanted and exact per-bar length
  isn't critical.

---

## 3. Length & lap

- `MaxBarLength` = the project's stock/transport limit (default `40'-0"`). Runs longer than
  this are split and lapped.
- Lap: default `LapMode=Factor LapFactor=40` (≈ ACI Class B for typical slab bars). Use
  `LapMode=Length LapLength=…` to pin an exact lap from the schedule.
- Keep `LapStagger=true` so adjacent splices don't align.

---

## 4. Free edges, openings, support zones

**Edge U-bars** — whenever `hints.needs_edge_u_bars`:
- `EdgeUBarsEnabled=true`, `EdgeUBarType` (match the edge mat), `EdgeUBarLeg` ≈ a tension
  development length, `EdgeUBarSelector=free` (the default targets `free_edge_indices`).
  Use explicit indices to cover only some edges.

**Opening trim** — for each id in `hints.openings_need_trim`:
- `OpeningTrimEnabled=true`; `OpeningExtraEachSide` extra bars per edge; keep
  `OpeningUBars=true` and `OpeningDiagonals=true` (corner cracking control). Default
  `OpeningSelector=all` already targets the flagged openings.

**Top strengthening over columns** — for each `context.supports_below` of `kind=Column`,
add rows to `slab-zones.csv` (both directions), using `hints.supports[*].suggested_strip_width_in`
as a starting strip width:
```csv
SlabMark,ZoneName,Face,Direction,BarType,Spacing,Shape,Extent
S-101,C-3 strip X,Top,X,#6,8,SupportMark:C-3:5'-0",6'-0"
S-101,C-3 strip Y,Top,Y,#6,8,SupportMark:C-3:5'-0",6'-0"
```
`Extent` ≈ the cantilever/development past the support face (engineer/brief driven).

---

## 5. Strictness & validation

- Emit only bar/hook names present in the dump's allow-lists. The plugin fails a slab (with
  the available list) on an unknown name — the others continue.
- Set `ExpectedThickness` from `thickness_in` so the plugin flags schedule/model mismatch.
- One row per Mark; duplicates → last wins (warning).

---

## 6. Output format

- Sparse CSV, one row per Mark, header of the fields you use.
- UTF-8 (BOM ok). `#` comment lines — **use them**: a short note per slab on why each
  choice was made (which edges free → U-bars, which openings trimmed, which supports
  zoned). Spaces inside selector lists, never commas.

---

## 7. Pre-handoff checklist

1. Every reinforced slab has a row; Marks unique and present in the dump.
2. Bottom mesh reproduces the spec; `TopMode` matches the design (OverSupports vs full mat).
3. Every `free_edge_indices` edge is covered by edge U-bars (or deliberately excluded).
4. Every `openings_need_trim` opening has trim enabled.
5. Each column in `supports_below` has top strengthening zones in both directions.
6. `MaxBarLength` / lap reflect the project; bar & hook names exist in the allow-lists.
7. Comment block explains each decision in plain language.
