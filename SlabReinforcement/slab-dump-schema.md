# Slab dump schema (`*_slabs.json`)

Field-by-field reference for the JSON produced by **Export Slabs**. The authoritative
definition is the POCO in [`Export/SlabDump.cs`](Export/SlabDump.cs); this document
explains the meaning and units. A worked example is
[`samples/example_slabs.json`](samples/example_slabs.json).

`schema_version: 1`. Keys are `snake_case`. **Units:** section dimensions and cover in
**inches**; elevations, plan coordinates and lengths in **feet** (Revit internal);
angles in **degrees** from world +X.

---

## Top level

| Key | Type | Meaning |
|---|---|---|
| `document` | `{title, path}` | Source model. `path` is null for unsaved docs. |
| `generated_at` | string | Local timestamp `yyyy-MM-ddTHH:mm:ss`. |
| `schema_version` | int | Currently `1`. |
| `units_note` | string | The units reminder above. |
| `levels` | `[{name, elevation_ft, id}]` | All levels, sorted by elevation. |
| `floor_types_in_use` | `{ "<type_id>": FloorType }` | Floor types of the exported slabs, keyed by element id. |
| `available_rebar_bar_types` | `[{name, nominal_diameter_in}]` | **Strict allow-list** — only emit these bar names in the CSV. |
| `available_rebar_hook_types` | `[{name}]` | Strict allow-list for hook names. |
| `warnings` | `[string]` | Per-slab failures encountered during export. |
| `slabs` | `[Slab]` | One entry per exported floor (see below). |

`FloorType` = `{family, type, id, thickness_in, structural_material}`.

---

## `slabs[i]`

### Identity & section
| Key | Type | Notes |
|---|---|---|
| `element_id` | long | Revit id of the floor. |
| `mark` | string? | Instance **Mark** — the CSV join key. |
| `comments` | string? | Instance **Comments** — the **primary reinforcement spec** source (free text, e.g. `B: #5@12 EW; T: #5@12 EW over cols`). Confirm the project's convention before bulk generation. |
| `family`, `type`, `type_id` | string/long | Floor type. |
| `level` | `{name, id, elevation_ft}` | The floor's base level. |
| `thickness_in` | double | From the type's compound structure (bbox-height fallback). |
| `top_elevation_ft`, `bottom_elevation_ft` | double | Top/bottom face world Z. |
| `is_structural` | bool | Floor's *Structural* flag. |
| `is_foundation` | bool | Category is Structural Foundation (mat/slab-on-grade). |
| `area_sf` | double | Plan area **less** openings. |
| `rebar_cover` | `{top, bottom}` | Each `{name, distance_in}` from the floor's cover settings (null if unset). |

### Frame & extent
- `local_basis` = `{origin_ft:[x,y,z], x_dir:[x,y], y_dir:[x,y], x_world_deg}`.
  **X** is along the slab's longest boundary edge; **Y** = 90° CCW from X. Bar
  directions in the CSV (`BottomX`, `TopY`, …) are relative to this frame.
- `bbox` = `{min_ft:[x,y], max_ft:[x,y]}` — axis-aligned plan bounds (world XY).

### `boundary[]` — outer edges, in loop order
| Key | Type | Notes |
|---|---|---|
| `index` | int | Edge index (selector value for `EdgeUBarSelector`). |
| `kind` | string | Geometry kind: `line` (arcs are chord-approximated this phase). |
| `start_ft`, `end_ft` | `[x,y]` | Edge endpoints. |
| `length_ft` | double | Edge length. |
| `mid_normal_world_deg` | double | **Outward** normal direction (deg). |
| `edge` | string | What it borders: `free` \| `beam` \| `wall` \| `slab`. |
| `adjacent` | `{kind, element_id, mark}`? | The supporting element (null for `free`). |

> **Free edges** (`edge: "free"`) are slab perimeters in open air — they get edge U-bars.

### `openings[]`
| Key | Type | Notes |
|---|---|---|
| `id` | int | 1-based; selector value for `OpeningSelector`. |
| `source` | string | `SketchLoop` \| `FloorOpening` \| `ShaftOpening`. |
| `element_id` | long | 0 for sketch loops. |
| `area_sf` | double | Plan area of the opening. |
| `needs_trim` | bool | True when a plan dimension ≥ 12" → trim bars + U-bars + diagonals. |
| `bbox` | `{min_ft, max_ft}` | Opening plan bounds. |
| `boundary` | `[{index, start_ft, end_ft, length_ft}]` | Opening edges. |

### `context`
| Key | Type | Notes |
|---|---|---|
| `supports_below` | `[{kind, element_id, mark, center_ft, width_in, depth_in}]` | `kind` = `Column` (point) \| `Wall` \| `Beam` (line). Drives top strengthening zones. |
| `walls_bounding` | `[{element_id, mark, boundary_indices}]` | Walls supporting boundary edges. |
| `beams` | same | Beams supporting boundary edges. |
| `slabs_coplanar` | same | Neighbouring slabs sharing an edge (continuity → lap, not a free edge). |
| `slab_above`, `slab_below` | `{element_id, mark, thickness_in}`? | Cross-level neighbours — **not computed yet** (null; Phase 5). |

### `hints` (advisory — you own the final call)
| Key | Type | Notes |
|---|---|---|
| `free_edge_indices` | `[int]` | Boundary indices that are free → candidate `EdgeUBarSelector`. |
| `needs_edge_u_bars` | bool | True if any free edge. |
| `openings_need_trim` | `[int]` | Opening ids that exceed the trim threshold. |
| `supports` | `[{mark, suggested_strip_width_in}]` | Per column under the slab; heuristic strip width for top strengthening. |
| `max_span_ft` | double | Larger bbox dimension. |
| `is_two_way` | bool | Aspect ratio ≤ 2. |
| `recommended_layers` | `[string]` | Default `["BottomX","BottomY","TopX","TopY"]`. |

---

See [`agent-config-guide.md`](agent-config-guide.md) for how to turn this into a CSV, and
[`slab-assignments-csv-guide.md`](slab-assignments-csv-guide.md) for the CSV fields.
