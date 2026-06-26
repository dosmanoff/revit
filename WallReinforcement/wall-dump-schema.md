# Wall dump schema (`*_walls.json`)

Field-by-field reference for the JSON produced by **Export Walls** (stage 1 of the agent
pipeline). The authoritative definition is the POCO in [`Export/WallDump.cs`](Export/WallDump.cs);
this document explains the meaning and units. Mirrors the SlabReinforcement dump convention.

`schema_version: 1`. Keys are `snake_case`. **Units:** thickness, cover, bar diameters and
opening sill/head in **inches**; lengths, heights, elevations and plan coordinates in **feet**
(Revit internal); angles in **degrees** from world +X. A wall is treated as a flat rectangular
panel in its own `(u, v)` frame: **u** runs along the wall length from its *start* end, **v**
upward from the base.

---

## Top level

| Key | Type | Meaning |
|---|---|---|
| `document` | `{title, path}` | Source model. `path` is null for unsaved docs. |
| `generated_at` | string | Local timestamp `yyyy-MM-ddTHH:mm:ss`. |
| `schema_version` | int | Currently `1`. |
| `units_note` | string | The units reminder above. |
| `levels` | `[{name, elevation_ft, id}]` | All levels, sorted by elevation. |
| `wall_types_in_use` | `{ "<type_id>": WallType }` | Wall types of the exported walls, keyed by element id. |
| `available_rebar_bar_types` | `[{name, nominal_diameter_in}]` | **Strict allow-list** — only emit these bar names in the brief. |
| `available_rebar_hook_types` | `[{name}]` | Strict allow-list for hook names. |
| `warnings` | `[string]` | Per-wall failures encountered during export. |
| `walls` | `[Wall]` | One entry per exported wall (see below). |

`WallType` = `{family, type, id, thickness_in, function, structural_material}`.
`function` = `Exterior | Interior | Foundation | Retaining | Soffit | CoreShaft`.

---

## `walls[i]`

### Identity & section
| Key | Type | Notes |
|---|---|---|
| `element_id` | long | Revit id of the wall. |
| `mark` | string? | Instance **Mark** — the primary brief join key. |
| `comments` | string? | Instance **Comments** — a possible reinforcement-spec source (free text). |
| `family`, `type`, `type_id` | string/long | Wall type. |
| `base_level` | `{name, id, elevation_ft}` | The wall's base constraint level. |
| `top_level` | `{name, id, elevation_ft}`? | The wall's top constraint level (null if unconnected / by-height). |
| `thickness_in` | double | Wall width. |
| `length_ft`, `height_ft` | double | Location-curve length; unconnected-or-resolved height. |
| `base_elevation_ft`, `top_elevation_ft` | double | Bottom / top of the wall, world Z (from the level's **ProjectElevation** + base offset — matches the LocationCurve Z). |
| `structural_usage` | string | `Bearing` \| `Shear` \| `Combined` \| `NonBearing`. |
| `function` | string | Type's *Function* (see above). |
| `is_structural` | bool | Wall's *Structural* flag. |
| `is_arc` | bool | Curved wall (its LocationCurve is an arc). Phase-1/2 reinforcement treats it via the chord. |
| `flipped` | bool | `Wall.Flipped` — affects which physical side the `normal_dir` (exterior) points to. |
| `rebar_cover` | `{exterior, interior, other}` | Each `{name, distance_in}` from the wall's cover settings (null if unset). `other` = top/bottom/end faces. |

### Frame & extent
- `local_basis` = `{origin_ft:[x,y,z], length_dir:[x,y], normal_dir:[x,y], length_world_deg}`.
  **origin** is the base corner at the start end, bottom of wall. **length_dir** is along the
  LocationCurve; **normal_dir** points interior→exterior (the wall facing). Opening `(u,v)` and
  junction `our_u_ft` are in this frame.
- `bbox` = `{min_ft:[x,y,z], max_ft:[x,y,z]}` — axis-aligned world bounds (3-D).
- `faces` = `[{side, gross_area_sf, net_area_sf}]` — `exterior` and `interior`; `net` is gross
  less openings (the same for both sides of a wall).

### `openings[]`
Detected from the wall's hosted inserts (doors, windows, rectangular wall openings) projected
into the wall `(u, v)` frame.

| Key | Type | Notes |
|---|---|---|
| `id` | int | 1-based; selector value for opening trim. |
| `insert_id` | long | The hosting door/window/opening element id. |
| `category` | string? | e.g. `Doors` \| `Windows` \| `Rectangular Straight Wall Opening`. |
| `family`, `type` | string? | Family symbol of the insert (for doors/windows). |
| `u_min_ft`, `u_max_ft`, `v_min_ft`, `v_max_ft` | double | Opening extent in wall coords. |
| `width_ft`, `height_ft` | double | `u_max−u_min`, `v_max−v_min`. |
| `sill_ft`, `head_ft` | double | `= v_min`, `= v_max` (above the wall base). |
| `needs_trim` | bool | True when both plan dimensions ≥ 12" → trim bars + diagonals. |
| `bbox` | `{min_ft, max_ft}` | Opening world AABB (3-D). |

### `junctions[]`
Wall-to-wall joints at THIS wall's two endpoints. A **T-junction** is reported only from the
**stem** wall (the one that ends on the other's interior); the through wall lists no junction.

| Key | Type | Notes |
|---|---|---|
| `kind` | string | `LCorner` (both walls end at the point) \| `TStem` (we end on the other's interior). |
| `at_end` | string | `start` \| `end` — which end of THIS wall the joint is at. |
| `our_u_ft` | double | `u` on this wall (0 or `length_ft`). |
| `other_wall_id` | long | The neighbouring wall. |
| `other_wall_mark` | string? | Its Mark. |
| `point_ft` | `[x,y]` | World joint point. |
| `other_dir` | `[x,y]` | Unit vector along the other wall, **away** from the joint. |

### `hints` (advisory — you own the final call)
| Key | Type | Notes |
|---|---|---|
| `recommended_faces` | `[string]` | Default `["exterior","interior"]` — both faces get a mesh. |
| `needs_opening_trim` | bool | Any opening exceeds the trim threshold. |
| `openings_need_trim` | `[int]` | Opening ids over the threshold. |
| `has_corners` | bool | An L-corner junction is present. |
| `has_t_junctions` | bool | A T-stem junction is present. |
| `thick_enough_for_ties` | bool | Thickness ≥ 10" → transverse ties are sensible. |
| `recommended_layers` | `[string]` | `["ExteriorVertical","ExteriorHorizontal","InteriorVertical","InteriorHorizontal"]`. |

---

See [`wall-brief-schema.md`](wall-brief-schema.md) for how to turn this into a reinforcement
brief that **Wall Reinforcement** consumes.
