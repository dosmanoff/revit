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
 ├─ lengths { maxBarLength, lap{mode,factor,length,stagger, fcPsi,fyPsi,epoxy,lightweight,adequateSpacing} }
 ├─ field  { mode, bottom{x,y}, top{coverage,x,y} }
 ├─ edges[]    per-segment edge treatment
 ├─ openings   trim policy
 └─ groups[]   arbitrary additional reinforcement
```

## `lengths` — max bar length & lap splices
`maxBarLength` (default `40'-0"`) caps each bar; longer runs are split and lapped. `lap.mode`:

| `lap.mode` | Lap length | Extra keys |
|---|---|---|
| `Factor` (default) | `factor · d_b` (default 40·d_b ≈ Class B rule of thumb) | `factor` |
| `Length` | a fixed value | `length` |
| `Aci` | **ACI 318-19** Class B tension splice ℓst = 1.3·ℓd (§25.4.2.3 / §25.5.2.1), computed per bar size and per layer (top mats get the ψt=1.3 top-bar factor automatically) | `fcPsi`, `fyPsi` (psi), `epoxy`, `lightweight`, `adequateSpacing` (default true) |

`stagger` (default true) offsets adjacent splices. For `Aci`, set `fcPsi`/`fyPsi` from the project (e.g. 5000 / 60000); an unknown bar name falls back to `factor·d_b`.

---

## `field` — the main mat (#1)
| Key | Values | Notes |
|---|---|---|
| `mode` | `Bars` \| `Sets` \| `AreaSystem` | **Sets** = one Rebar element per run with a spacing layout, shown as the representative (middle) bar; laps by splitting the set. **AreaSystem** = native area rebar (openings excluded via inner loops). **Bars** = one Rebar per bar. |
| `bottom.x` / `bottom.y` | `{barType, spacing}` | Bottom mat in local X / Y. |
| `top.coverage` | `None`\|`Continuous`\|`OverSupports`\|`Edges` | `OverSupports`/`Edges` place top steel only where needed (via `groups`/`edges`); `Continuous` = full top mat. |
| `top.x` / `top.y` | `{barType, spacing}` | Used when `coverage = Continuous`. |

## `edges[]` — per-boundary-segment treatment (#3)
**Every** boundary edge is addressable — the dump's `boundary[]` enumerates them all with a stable
`index`, geometry (`start_ft`/`end_ft`/`length_ft`/`mid_normal_world_deg`), classification
(`edge`: `free`\|`beam`\|`wall`\|`slab`) and the adjacent element's `mark`. **Enumerate the edges
yourself** and give each its treatment; don't rely on coarse `"free"`/`"all"` shorthands when the
engineer wants control. An edge **not covered by any rule gets no edge bars** — that is how you say
"no П along these". Use `treatment.type: None` only to *document* a deliberate skip.

`segments` selects which edges a rule applies to:

| `segments` | Targets |
|---|---|
| `"0 1 4 11"` (space-separated indices) | **Preferred** — exactly those `boundary[].index` edges. |
| `"free"` | all `boundary[].edge=="free"` (shorthand). |
| `"all"` | every boundary edge (shorthand). |

| `treatment.type` | Meaning |
|---|---|
| `UBar` | Closing U-bar wrapping the edge top-to-bottom (`barType`, `spacing`, `leg`, `face`). Stays inside the slab. |
| `Bend90` | Mat bars bend 90° at the edge (`leg`, `direction` up/down, optional `anchorInto`+`anchorLen`). |
| `Straight` | No extra edge bar — the field mat just runs to the edge (cover only). |
| `IntoSupport` | Bars continue and anchor into the beam/wall (`anchorInto`, `anchorLen`). Projects *past* the edge, so use only where there is a support to anchor into. |
| `None` | No edge bars (explicit skip). |

> Procedure: walk `boundary[]`, group the indices by the treatment you want, emit one rule per group.
> Free/cantilever edges → `UBar` or `Bend90`. Beam/wall edges → `IntoSupport`/`Bend90` anchored into the
> support, **or** `UBar` if the engineer wants a closing edge bar there too, **or** leave them out for none.

Worked example (closes free edges with U-bars; the engineer also wants U-bars on the beam-supported
stretch; one short notch deliberately left bare):
```json
"edges": [
  { "_note": "free perimeter (boundary[].edge=='free'), minus notch 7",
    "segments": "0 1 2 3 4 5 6 8 9 14 15 16 20 21 22 27 28 29 30 31 32 33 34 35",
    "treatment": { "type": "UBar", "barType": "#5", "spacing": 12, "leg": "1'-6\"", "face": "both" } },
  { "_note": "edges on beam B1.1 (boundary[].edge=='beam') — closing U-bars",
    "segments": "10 11 12 13 17 18 19 23 24 25 26", "support": "beam", "adjacent": "B1.1",
    "treatment": { "type": "UBar", "barType": "#5", "spacing": 12, "leg": "1'-6\"", "face": "both" } }
  // edge 7 appears in no rule → no edge bars there
]
```

## `openings`
`trim`: **`auto`** (default) \| `all` \| `none` \| index list. Plus `barType`, `extraEachSide`,
`uBars`, `diagonals`. You don't list openings — they're detected from the slab **face geometry**
(any shape/authoring method, arcs tessellated) and **auto-classified**, so trim lands only where
it's useful:

- **Trim** — an isolated small/medium penetration → trim bars + diagonals.
- **Shaft** — a large opening (stair/elevator, ≥ 3.5 ft or ≥ 16 sf) → NOT trimmed (it gets edge
  reinforcement, like a free edge).
- **EdgeAdjacent** — hard against the slab edge or another big opening → NOT trimmed (redundant).

`auto` trims only **Trim**-class openings; the dump reports each opening's `class` + `class_reason`
so you can override with `all` / `none` / explicit ids when the engineer disagrees.

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
3. **Edges**: walk the dump's `boundary[]` and **enumerate the edges by `index`** — group them into
   rules by the treatment you want (free/cantilever → `UBar`/`Bend90`; beam/wall → `IntoSupport`/`Bend90`
   anchored, or `UBar` if a closing edge bar is wanted there). Edges you leave out of every rule get no
   edge bars. Prefer explicit index lists over `"free"`/`"all"` so nothing is silently skipped.
4. **Openings**: usually `auto`.
5. **Groups**: encode every additional bar group in full — top-over-supports, bottom bands, and any
   dowels/starters into adjacent or future elements (walls starting above, stairs).
6. Emit only valid bar/hook names (from the dump's available lists). Keep it explicit and complete.

## What the generator does automatically (you don't specify)
- **Void detection** from the real slab face — openings of any shape/method, arcs tessellated;
  no rebar is placed where there's no concrete.
- **Field clipping** around every opening; in `Sets` mode the mat is split into uniform rebar
  **sets** (representative/middle bar) that lap by splitting.
- **Opening classification** (Trim / Shaft / EdgeAdjacent) — `trim: auto` uses it.
- **Layer tagging** `SR:{config}:{slabId}:{layer}` so Slab Views can isolate Layer 1–4 and re-runs
  clean idempotently.

**Implementation status (in the build):** schema + loader (PR-17); field Bars/**Sets**/AreaSystem
(PR-08/11/18); per-segment edges + groups/dowels + smart opening trim + brief consumption (PR-19);
geometry+adjacency export incl. slab above/below (PR-16/20); Slab Views Layer 1–4 + schedules +
sheets + 3D cage + bending details (PR-13/14/15/21). Edge anchorage into beams/walls and the
AreaSystem-with-holes partition remain refinements.
