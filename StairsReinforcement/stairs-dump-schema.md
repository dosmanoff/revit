# StairsDump JSON — schema reference

Output of **Export Stairs** (`schema_version: 1`). One file per export; `stairs[]` holds one
entry per native `Stairs` element and (if floors were selected) one entry for the floor-modelled
group. A worked example is `example_stairs.json`.

**Units convention** (also shipped in `units_note`): sections & cover in **inches** (`*_in`),
positions / elevations / lengths / areas in **feet** (Revit internal), angles in **degrees** from
world +X. Vectors are bare number arrays (`[x,y]` or `[x,y,z]`).

## Top level
| field | type | meaning |
|---|---|---|
| `document` | `{title, path}` | model title and saved path (`path` null if unsaved) |
| `generated_at` | string | ISO timestamp |
| `schema_version` | int | `1` |
| `units_note` | string | the units reminder above |
| `levels` | `[{name, elevation_ft, id}]` | all levels, sorted by elevation |
| `stair_types_in_use` | `{ "<type_id>": {family, type, id} }` | types of the exported hosts |
| `available_rebar_bar_types` | `[{name, nominal_diameter_in}]` | **strict allow-list** — use these names verbatim |
| `available_rebar_hook_types` | `[{name}]` | strict allow-list |
| `warnings` | `[string]` | per-element extraction issues |
| `stairs` | `[Stair]` | the stairs |

## Stair
| field | type | meaning |
|---|---|---|
| `element_id` | long | `Stairs` id, or the first floor id for a floor group |
| `mark` | string? | **primary reinforcement-brief source** — the engineer's `Mark`/`Comments` spec |
| `comments` | string? | free-text spec (see `mark`) — confirm which field the project uses |
| `source` | string | `"stairs"` or `"floors"` |
| `rebar_host_ok` | bool | every component is a valid Revit rebar host (false ⇒ native Stairs can't host — flag it) |
| `flights` | `[Flight]` | inclined waist spans |
| `landings` | `[Landing]` | horizontal landing slabs |
| `warnings` | `[string]?` | per-stair issues |

## Flight
Inclined waist slab. `local_basis` is the slope frame: `u_dir` climbs up the slope, `w_dir` is
horizontal across the width, `n_dir` is the waist normal (points up). A flight longitudinal bar
runs along `u_dir`, sits at cover along `n_dir`, and repeats across the width along `w_dir`.

| field | type | meaning |
|---|---|---|
| `index` | int | flight order within the stair |
| `component_id` | long | `StairsRun` id or `Floor` id |
| `source` | string | `"run"` or `"floor"` |
| `rebar_host_ok` | bool | this component hosts rebar |
| `waist_in` | number | waist thickness (in). For floors = slab thickness. Native: from run type, else assumed 6″ (verify) |
| `width_ft` | number | flight width |
| `run_length_ft` | number | horizontal going length |
| `slope_length_ft` | number | length measured up the slope |
| `total_rise_ft` | number | vertical rise |
| `slope_deg` | number | pitch above horizontal |
| `riser_count` / `tread_count` | int | steps (0 for floor-modelled flights) |
| `tread_in` / `riser_in` | number | going / rise per step (in) |
| `local_basis` | `{origin_ft[3], u_dir[3], w_dir[2], n_dir[3], run_world_deg}` | slope frame (see above) |
| `bbox` | `{min_ft[3], max_ft[3]}` | world AABB |
| `lower_support` / `upper_support` | `Support?` | what each end bears on |

## Landing
| field | type | meaning |
|---|---|---|
| `index` | int | landing order |
| `component_id` | long | `StairsLanding` id or `Floor` id |
| `source` | string | `"landing"` or `"floor"` |
| `thickness_in` | number | slab thickness (in) |
| `elevation_ft` | number | top elevation |
| `area_sf` | number | plan area |
| `local_basis` | `{origin_ft[2], x_dir[2], y_dir[2], angle_world_deg}` | horizontal frame; X along the longest edge |
| `bbox` | `{min_ft[3], max_ft[3]}` | world AABB |
| `boundary` | `[[x,y], …]` | plan loop (feet) |
| `supports` | `[Support]` | walls/beams under the landing |
| `connects_flights` | `[int]` | indices of the flights this landing joins |

## Support
`{ kind, element_id, elevation_ft }` where `kind ∈ slab | beam | wall | foundation | landing |
stairs | none`. A `landing` kind at a flight end marks a flight↔landing fold (knee detail).
