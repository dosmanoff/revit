# Slab reinforcement brief — JSON schema & agent guide

The **structured JSON brief** the agent produces from the model + the project's reinforcement
task (PDF) + this guide, and that **Generate Slab Rebar** consumes. It replaces the flat CSV:
it can describe **per-boundary-segment** edge treatment and **arbitrary, fully detailed** rebar
groups (incl. dowels into walls/stairs) — i.e. it is *deliberately over-detailed* so the
generator can place anything without guessing.

Authoritative definition: the POCO in [`Config/SlabBrief.cs`](Config/SlabBrief.cs). Worked example:
[`samples/slab-brief-example.json`](samples/slab-brief-example.json). `schemaVersion: 1`, camelCase
keys, string enums. **Lengths** are a number (per `units`) or a feet-inches string (`"1'-6\""`).

```
slabs[]                 one entry per slab, matched by `mark` or `elementId`
 ├─ cover {top,bottom,side}
 ├─ lengths { maxBarLength, lap{mode,factor,length,stagger} }
 ├─ field  { mode, bottom{x,y}, top{coverage,x,y} }
 ├─ edges[]    per-segment edge treatment
 ├─ openings   trim policy
 └─ groups[]   arbitrary additional reinforcement
```

---

## `field` — the main mat (#1)
| Key | Values | Notes |
|---|---|---|
| `mode` | `Bars` \| `Sets` \| `AreaSystem` | **Sets** = one Rebar element per run with a spacing layout, shown as the representative (middle) bar; laps by splitting the set. **AreaSystem** = native area rebar (openings excluded via inner loops). **Bars** = one Rebar per bar. |
| `bottom.x` / `bottom.y` | `{barType, spacing}` | Bottom mat in local X / Y. |
| `top.coverage` | `None`\|`Continuous`\|`OverSupports`\|`Edges` | `OverSupports`/`Edges` place top steel only where needed (via `groups`/`edges`); `Continuous` = full top mat. |
| `top.x` / `top.y` | `{barType, spacing}` | Used when `coverage = Continuous`. |

## `edges[]` — per-boundary-segment treatment (#3)
Each entry targets one or more boundary segments (`segments`: `"free"` \| `"all"` \| indices like `[1]`
or `"0 2"`), states what it borders (`support`: free/beam/wall/slab, `adjacent` = its mark) and how the
edge is reinforced:

| `treatment.type` | Meaning |
|---|---|
| `UBar` | Closing U-bar wrapping the edge top-to-bottom (`barType`, `spacing`, `leg`, `face`). |
| `Bend90` | Mat bars bend 90° at the edge (`leg`, `direction` up/down, optional `anchorInto`+`anchorLen`). |
| `Straight` | Bars run to the edge (cover only). |
| `IntoSupport` | Bars continue and anchor into the beam/wall (`anchorInto`, `anchorLen`). |
| `None` | No edge bars. |

> Drive the segment selection and support from the dump's `boundary[*].edge`/`adjacent`. Free edges →
> `UBar` or `Bend90`; beam/wall edges → `IntoSupport` or `Bend90` anchored into the support.

## `openings`
`trim`: `auto` (size threshold) \| `all` \| `none` \| index list. Plus `barType`, `extraEachSide`,
`uBars`, `diagonals`. Openings (any geometry) are detected from the slab face — you don't list them,
you only set the policy.

## `groups[]` — arbitrary additional reinforcement (#4)
A fully-specified bar group. Typical (top over supports, bottom in span) **and** non-typical
(dowels into a wall that starts above, stair starters) are all expressed here.

| Key | Values / notes |
|---|---|
| `layer` | `Bottom`\|`Top`\|`Support`\|`Dowel`\|… (tag + which view layer). |
| `barType`, `spacing` / `count` | Bar size and either a spacing across the region or a fixed count. |
| `shape` | `Straight`\|`L`\|`U`\|`Hook90`\|`Hook180`\|`Custom`. |
| `direction` | `{kind: Axis, axis:X/Y}` \| `{kind:World, deg}` \| `{kind:AlongEdge, edge}` \| `{kind:TowardSupport, support}`. |
| `region` | `SupportStrip{support,width,extent}` \| `BBox[x1,y1,x2,y2]` \| `Polygon[…]` \| `EdgeRange{segment,from,to}` \| `Line{lineFrom,lineTo}`. |
| `face` | `Top`\|`Bottom`\|`Mid`. |
| `length` | null = derived from region; else explicit. |
| `anchor` | `{start,end}` each `{type: Straight/Hook90/Hook180/Bend, len}`. |
| `dowel` | Out-of-plane starters: `{into: Wall/Stair/SlabAbove/Beam, target, embedLen, projectLen, bend, direction, angleDeg}`. |

---

## Agent procedure (summary)
1. Read the slab dump + the project task. Match each slab by `mark`/`elementId`.
2. **Field**: pick `mode` (Sets unless told otherwise); bottom EW from the task; `top.coverage`.
3. **Edges**: for every boundary segment, set the treatment from what it abuts (free → U-bar/bend;
   beam/wall → into-support/bend). Be explicit per segment — the dump gives you `edge` + `adjacent`.
4. **Openings**: usually `auto`.
5. **Groups**: encode every additional bar group in full — top-over-supports, bottom bands, and any
   dowels/starters into adjacent or future elements (walls starting above, stairs).
6. Emit only valid bar/hook names (from the dump's available lists). Keep it explicit and complete.

**Implementation status:** the schema and loader land in PR-17. The engine consumes it across
PR-18 (field Bars/Sets/AreaSystem), PR-19 (per-segment edges) and PR-20 (groups/dowels).
