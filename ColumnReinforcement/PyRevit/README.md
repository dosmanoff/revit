# Column Dump (pyRevit extension)

Companion tool for the **ColumnReinforcement** plugin. Reads a Revit model and writes a JSON
dump of every structural column — geometry, level, neighbouring slabs, and the column directly
above/below it — so that Claude (or another AI agent) can produce
[`assignments.csv`](../assignments-csv-guide.md) for the plugin.

The C# plugin doesn't read this JSON. The flow is one-way:

```
Revit model ──[pyRevit: Dump Columns]──► columns.json
                                              │
                                              ▼
                              Claude reads columns.json + schedule
                                              │
                                              ▼
                              Claude writes assignments.csv
                                              │
                                              ▼
                          ColumnReinforcement plugin → From CSV → Run
```

## What's in the dump

Per column:

- `mark`, `family`, `type`, `type_id`, `element_id`
- `section` (`Rectangular` / `Round`), `width_in` / `depth_in` (or `diameter_in`), `rotation_deg`
  - **Canonical orientation:** for rectangular columns the local frame is canonicalised so **LocalX is always the shorter in-plan side** (`width_in ≤ depth_in`), exactly mirroring `Domain/ColumnGeometry.cs`. When the modelled column is wider than deep the script rotates the frame +90° about Z (`newX = oldY`, `newY = −oldX`, right-handedness preserved) and swaps width/depth. `rotation_deg` is reported for this canonical frame, so it can differ by 90° from the raw Revit instance rotation. Everything below — `faces`, neighbour `face_insets_in`, `relative.offset_local_*` — is expressed in this same canonical frame.
- `faces` — explicit per-face geometry in the canonical frame, keyed `+x` / `-x` / `+y` / `-y` (**`null` for round columns**). These keys are **exactly the engine's `LongTopModes` face selectors** — what the agent writes as `+x:BentToSlab` lands on the bars along that face. Each face carries:
  - `plan_length_in` — length of that face in plan
  - `kind` — `long` (the ±X faces, perpendicular to the short LocalX, spanning the depth) or `short` (the ±Y faces, spanning the width)
  - `outward_normal_world_deg` — the outward normal direction in world degrees, so the agent can correlate a face with a neighbour's overhang or a slab edge
- `base`: level name, elevation (ft), plan X/Y (ft)
- `top`: level name, elevation (ft)
- `height_ft`
- `comments` — the Revit **Comments** instance parameter. Structural engineers commonly encode the rebar schedule inline here, e.g. `12#7 + #4@10" STIRRUP + #4@10" TIE`. **Read this first** when generating the CSV — it's the most direct source of truth.
- `rebar_cover.{top, bottom, other}` — Revit's native per-face `RebarCoverType` overrides as `{name, distance_in}` or `null`. When set, **use these instead of the plugin's 1.5″ default** for `CoverSides` / `CoverEnds`.
- `parameters` — dict of every instance parameter with a non-null value, keyed by display name. Catches custom shared parameters like `Corner Mark`, `Column.Reaction`, etc. that vary by project. Doubles render as `{raw, display}`; ElementId references as `{id, name}`.
- `context.slab_below` — `StructuralFoundation` or `Floor`, with thickness (drives `DowelOnlyFoundation`)
- `context.slab_above` — `Floor` (drives `SpliceForm=Bent`)
- `context.column_above` / `column_below` — neighbour column **with its true geometric relation** to this column:
  - `relative.offset_local_x_in`, `relative.offset_local_y_in` — neighbour center offset in *this* column's local frame (resolves non-coaxial stacks)
  - `relative.relative_rotation_deg`, `relative.axis_aligned`, `relative.axis_swap`
  - `face_insets_in` — per-face inset of upper inside lower (rect-rect, axis-aligned): `plus_x_in`, `minus_x_in`, `plus_y_in`, `minus_y_in`. **Negative = upper overhangs that face.**
  - `radial_inset` — for round-round stacks: `radial_inset_in` (closest gap), `max_inward_in` (far-side traverse), `center_offset_in`
- `hints` — pre-computed convenience flags. Hints are advisory; the agent still owns the final decision per the [CSV guide §6](../assignments-csv-guide.md#6-for-schedule-analyzer-ai-agent--extraction-rules).
  - `is_ground_level`, `is_roof_level`
  - `foundation.has_slab_below`, `foundation.is_structural_foundation`, `foundation.needs_dowels`, `foundation.dowel_only_foundation_recommended`
  - `splice.form_hint`: `Straight` / `Cranked` / `Bent` / `StraightFromLowerColumn` / `null`
  - `splice.non_coaxial`, `splice.overhang_faces`, `splice.upper_inset_in`, `splice.max_inward_inset_in`
  - `splice.cranked_check`: ACI 318 §10.7.4.1 feasibility — `feasible_aci_1_to_6`, `min_required_vertical_in_at_1to6`, `available_vertical_in`, `available_slope_1_to_n`
  - `splice.needs_starters_from_lower_column` — `true` when Cranked is geometrically infeasible (slope > 1:6 or upper overhangs); the engineer must place straight dowel-style starters from inside the lower column up into the upper column (see [Splice scenarios](#splice-scenarios) below). **Plugin has no first-class form for this** — flag is informational.
  - `splice.notes` — human-readable explanations for the chosen form

Plus document-level info that helps the agent only emit valid CSV:

- `levels` — names + elevations
- `available_rebar_bar_types` — every `RebarBarType.Name` actually loaded (e.g. `#3`, `#4`, … `#11`)
- `available_rebar_hook_types` — every `RebarHookType.Name`
- `available_column_family_types` — every column type symbol present (id + family + type only — reference catalogue)
- `column_types_in_use` — keyed by stringified `type_id`. Every column type that at least one dumped column uses, with **all type-level parameters**. Per-column records carry `type_id` for cross-reference; type params aren't duplicated on every column.
- `warnings` — duplicate Marks, columns with no Mark, geometry errors, …

Sections in inches; elevations and plan offsets in feet (Revit internal). See `units_note` in the file.

## Install

Three ways, easiest first.

### A. Drop into the user extensions folder

```
%APPDATA%\pyRevit\Extensions\
```

Copy `ColumnDump.extension/` into that folder. Restart Revit (or click **Reload** on the pyRevit ribbon).

### B. Register this repo path as a custom extensions directory

pyRevit ribbon → **Settings** → **Custom Extension Directories** → **+** → pick
`L:\My Drive\claude\revit\ColumnReinforcement\PyRevit\` → **Save & Reload**.

(Note: pyRevit recurses into the chosen folder looking for `*.extension` bundles.)

### C. CLI

```powershell
pyrevit extensions paths add "L:\My Drive\claude\revit\ColumnReinforcement\PyRevit"
```

## Run

Revit ribbon → **Column Rebar** tab → **Analyze** panel → **Dump Columns**.

A save dialog appears. Default file name is `<ProjectName>_columns.json` next to the `.rvt` file.

Output console shows column count, warning summary, and the absolute file path.

## Hand off to Claude

In your terminal, open Claude Code in the repo root and ask something like:

> Read `<path>\Project1_columns.json` and write `samples/assignments.csv` per
> `assignments-csv-guide.md`. Use only bar types listed under `available_rebar_bar_types`.
> For columns sitting on a `StructuralFoundation`, enable dowels (L-form, `#11`, lap 30",
> embed 6", leg 12"). For columns with a smaller column above, use Cranked splice with
> the suggested `SpliceUpperInset`. For roof columns with a slab above, use Bent splice.

Claude will:

1. Group columns by Mark / level.
2. Match each column to your schedule (or apply your stated defaults).
3. Validate Mark uniqueness and bar-type availability.
4. Write a sparse CSV (only non-default fields).

You then load that CSV in the **ColumnReinforcement** plugin: **From CSV** → pick file → review the
validation table → **Run**.

## Splice scenarios

The script flags the four scenarios that drive `SpliceForm` in the CSV:

| Scenario | `splice.form_hint` | Plugin CSV |
|---|---|---|
| Same-size column above (or upper bigger on every face) | `Straight` | `SpliceForm=Straight`, `SpliceLap`, `SpliceExt` |
| Smaller column above + ACI 1:6 slope achievable | `Cranked` | `SpliceForm=Cranked`, `SpliceUpperInset = splice.upper_inset_in` |
| No column above, but slab above | `Bent` | `SpliceForm=Bent`, `SpliceBentLeg` |
| Smaller column above but Cranked **infeasible** (slope > 1:6, or overhang) | `StraightFromLowerColumn` | **Not yet supported by the plugin** — engineer to place straight dowel-style starters from inside the lower column up into the upper column |
| Top of building (no column above, no slab above) | `null` | Omit splices for this Mark |

`splice.cranked_check` gives the numbers behind the decision so the engineer can override:
`max_inward_inset_in` (worst face traverse), `available_vertical_in` (column height minus a 30″ lap budget), `min_required_vertical_in_at_1to6` (= 6 × inset per ACI), and `available_slope_1_to_n` (≥ 6 means OK).

The 30″ budget is a conservative default (24″ upper-leg lap + 6″ lower bend offset, both stock plugin defaults). When your project's splice config uses bigger laps, expect a few false negatives — re-check rows flagged `StraightFromLowerColumn` if the column is tall.

## Re-running

Each run is a full snapshot — overwrite the JSON file as the model evolves. There's no
state inside the script; nothing is written into the Revit model.

## Notes / limits

- Slanted columns are skipped (the plugin doesn't support them either).
- The column-above / column-below detector uses an 18″ XY tolerance and ~3/4″ Z tolerance.
  Columns whose centers shift more than 18″ between stories won't be paired — they'll
  get `splice.form_hint = Bent` or `null` based only on the slab above. Adjust
  `XY_NEIGHBOUR_TOL_FT` in the script if your project shifts columns more aggressively.
- Slab thickness is taken from the structural slab parameter when present, otherwise from
  bounding-box height.
- Section detection mirrors `Domain/ColumnGeometry.cs` (bottom face edge analysis →
  Rectangular if any straight edge, Round if all arcs). Works for the standard OOTB
  concrete column families; weird custom shapes fall back to AABB and report as
  Rectangular.
- Works with both IronPython 2.7 (default pyRevit) and CPython 3 (pyRevit CPython runtime).
- Tested target: Revit 2025.
