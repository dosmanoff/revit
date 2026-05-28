# Agent guide — turning a ColumnDump into `assignments.csv`

This is the operating manual for the **config-generation agent**: given a
ColumnDump JSON (schema_version ≥ 4), produce a sparse `assignments.csv` that the
ColumnReinforcement plugin loads via **From CSV → Run**.

It is a *decision procedure*, not a field reference. For the meaning/units of each
CSV column, see [`assignments-csv-guide.md`](assignments-csv-guide.md). This guide
tells you **what to decide and how**, encoding the geometry reasoning that is easy
to get wrong (rotated/offset section changes, per-bar split, starter dowels).

US norms: **ACI 318-19, Imperial (inches), non-seismic unless told otherwise.**

---

## 0. The flow

```
Revit model ──[pyRevit: Dump Columns]──► columns.json  (you read this)
                                              │
                                    YOU = generation agent
                                              │
                                              ▼
                                       assignments.csv  (you write this)
                                              │
                              ColumnReinforcement plugin → From CSV → Run
```

One row per **Mark**. Every column in the dump that you reinforce needs a row.
Empty cells mean "use the plugin default" — keep the CSV **sparse**: only emit a
field when it differs from the default or is needed for clarity/validation.

---

## 1. Read the inputs (and where each datum lives)

| Datum | Source in the dump | Notes |
|---|---|---|
| Section + size | `section`, `width_in`, `depth_in` (or `diameter_in`) | Already in the **canonical frame**: `width_in ≤ depth_in`, LocalX = short side. Cross-check against `column_types_in_use[type_id]` (`b`/`h`). |
| **Rebar spec** | `comments` (Revit *Comments* instance param) | **Primary source.** Free text like `20#8 + #4@10" STIRRUPS` → 20 longitudinal #8, ties #4 @ 10". **Confirm the field with the user before bulk-generating** — some projects put the spec in a custom parameter (scan `parameters` for things like `Rebar`, `Long Reinf`, etc.) |
| Cover | `rebar_cover.{top,bottom,other}` | Use these when present; else default 1.5″ (`CoverSides`/`CoverEnds`). |
| Slab below / above | `context.slab_below` / `context.slab_above` | `thickness_in` drives `StirrupOffsetTop` and foundation embedment. |
| Column above / below | `context.column_above` / `context.column_below` | `relative.*` and `face_insets_in` drive the top-mode split and starter dowels — see §4. |
| Faces | `faces.{+x,-x,+y,-y}` | `kind` (long/short) + `outward_normal_world_deg`. The keys are **exactly** the `LongTopModes`/`DowelPositions` face selectors. |
| Pre-computed hints | `hints.splice`, `hints.foundation` | Advisory. `splice.cranked_check.feasible_aci_1_to_6`, `form_hint`, `needs_starters_from_lower_column`. You own the final call. |
| Available bar/hook types | `available_rebar_bar_types`, `available_rebar_hook_types` | **Strict** — only emit names that appear here. |
| Levels | `levels` | For sanity / roof detection. |

---

## 2. Bar size, count, and layout

From the Comments spec, e.g. `20#8`:

- **`LongBarType`** = `#8` (must be in `available_rebar_bar_types`).
- **Total bars** N = 20. Solve the perimeter relation for a rectangular cage:
  **N = 2·(nW + nD) − 4**, where `nW = LongBarsW` (bars on the **short** ±Y faces,
  spread along X) and `nD = LongBarsD` (bars on the **long** ±X faces, spread along Y).
  Pick nW, nD so spacing is roughly even: `spacing ≈ width/(nW−1) ≈ depth/(nD−1)`.
  - 24×36, N=20 → nW=5, nD=7 (6″ both ways).
  - 18×24, N=12 → nW=4, nD=4.
  - 16×16, N=12 → nW=4, nD=4.
- Round columns: `LongBarsAround = N`.
- Ties: `StirrupBarType` + `StirrupSpacing` from the Comments (`#4@10"` → `#4`, `10`).

---

## 3. Cage enumeration & the canonical frame (you MUST internalise this)

Indices for `LongTopModes`/`DowelPositions` follow the engine's layout. Rectangular,
counts nx=`LongBarsW`, ny=`LongBarsD`:

```
indices 0 … nx-1        : −Y face (y = yMin), left→right along X
indices nx … 2nx-1      : +Y face (y = yMax), left→right along X
then, row by row (j=1..ny-2): −X side bar, then +X side bar
```

So corners are `0`, `nx-1`, `nx`, `2nx-1`. After canonicalisation: **±X = long
faces, ±Y = short faces** (confirm with `faces[*].kind`).

Face selectors classify a bar by its extreme coordinate: `+x` = x at max, `-x` = x
at min, `+y`/`-y` likewise. **A corner belongs to two faces.** Prefer face/`corners`/
`edges` keywords over raw indices when a whole face shares a mode; use explicit
indices when a face is split (see §4.2).

---

## 4. Longitudinal top mode — the core decision

For each column decide, **per bar**, one of: `Straight` (to column top), `Cranked`
(bar bends over and penetrates up into the column above), `BentToSlab` (90° hook
into the slab above). Drive it from `context`:

### 4.1 No column above
- **Slab above** (`context.slab_above` ≠ null, or it's a roof level) →
  `LongTopDefault=BentToSlab`. Bend **outward** (`LongTopBentOutward=true`, the
  default) so opposite-face legs fan into the slab instead of crossing. Leg length
  ≈ development of a hooked bar (~16–20″ for #7/#8).
- **No slab above** (true open top) → `Straight` (or just omit the column).

### 4.2 Column above (a splice)
Determine the **upper column's footprint in THIS column's canonical frame** from
`context.column_above.face_insets_in`:

```
footprint X: [ -W/2 + minus_x , +W/2 - plus_x ]
footprint Y: [ -D/2 + minus_y , +D/2 - plus_y ]
```
(`face_insets_in` is the inset of the upper column inside the lower, per face. A
**negative** inset means the upper column **overhangs** that face.)

Now, for each cage position (x, y):
- **Inside the footprint** → the bar can continue up → `Cranked` (it cranks by one
  bar diameter to lap the upper cage and penetrates up).
- **Outside the footprint** → no column above that bar → `BentToSlab` (outward).

Shortcuts:
- Upper column **same size & coaxial** (all insets ≈ 0) → all bars `Cranked`
  (standard 1·d_b offset-bend lap splice).
- Upper column **uniformly smaller, centred & not rotated** → usually the corner +
  near-corner bars fall outside → use `corners:BentToSlab` etc., or the per-bar test.
- Upper column **shifted / rotated / much smaller** → the footprint covers only part
  of the cage → **split per index**. Write `LongTopDefault` to the majority mode and
  override the minority by index/face in `LongTopModes`.

**Express the split with face selectors when a whole face is in/out; with explicit
indices when a face is split** (e.g. a long face that's half under the upper column).
Index precedence beats keywords, so you can combine: default + face keywords + a few
index overrides.

### 4.3 Cranked feasibility & parameters
A bar may only `Cranked` if the offset is achievable at ACI 318-19 §10.7.4.1's
**1:6** max slope within the available height. Check `hints.splice.cranked_check`:
`feasible_aci_1_to_6`. For a **per-bar** crank the relevant offset is that bar's own
shift (often just 1·d_b for a same-position lap), **not** the worst-face inset the
hint computed for the whole section — so per-bar cranks under the footprint are
usually feasible even when the global hint says "infeasible".

Parameters (defaults that work well):
- `LongCrankUpperInset` = **one bar diameter** (#8→1.0, #7→0.875, …) for a standard
  offset-bend lap. Larger only if the bar must physically shift to reach the upper cage.
- `LongCrankSlope` = `6` (the ACI cap).
- `LongCrankLowerBendOffset` = **≥ the slab-above thickness** (e.g. 12 for a 10″ slab)
  so the inclined portion stays below the joint, not inside the slab.
- `LongCrankPenetration` = compression lap ≈ **30·d_b** (#7→~27→use 30; #8→30).

---

## 5. Starter dowels for the column above (the cross-column rule)

**This is the rule people miss.** Whenever a column bends part of its cage into the
slab (§4.2 outside-footprint bars), the column **above** loses its continuing bar at
those plan positions. Those upper-column bars must be started with **straight dowels
out of the lower column**.

On the **upper** column's row:
- `DowelsEnabled=true`, `DowelHost=Column`, `DowelForm=Straight`.
- `DowelBarType` = the upper cage size (leave matching `LongBarType`).
- `DowelExt` = lap up into the upper cage (~30·d_b). `DowelEmbed` = lap down into the
  lower column (~30·d_b; must be ≤ lower column height).
- **`DowelPositions`** = the faces/indices of the upper cage that have **no**
  continuing (cranked) bar coming from below = the **complement** of the lower
  column's cranked faces, **mapped through the relative rotation**.

### 5.1 Mapping the rotation (do this carefully)
`context.column_below.relative.relative_rotation_deg` (and `axis_swap`) tells you how
the lower column's frame is turned relative to this one. A lower-column face maps to
an upper-column face rotated by that angle. **Dowel the upper faces that the lower
column's cranked bars do NOT reach.**

Worked mappings from the C1 stack (use as templates):
- **C1.2→C1.3** (lower C1.2 rotated +90° vs C1.3): C1.2 cranks its **±X** faces →
  those land on C1.3's **±Y** faces → C1.3 dowels the complement **`+x -x`**.
- **C1.1→C1.2** (lower C1.1 rotated −90° vs C1.2): C1.1 cranks its **−Y** face (→
  C1.2's **+X**) and its **±X** faces (→ C1.2's **±Y**) → the only uncovered C1.2 face
  is **−X** → C1.2 dowels **`-x`**.

When unsure of the sign, reason physically: the upper-column face sitting over the
part of the lower column where bars **bent** (not cranked) is the one needing dowels.
A dowel and a cranked bar coexisting on the same bar are fine — the dowel laps the
**bottom**, the crank leaves the **top**; just don't dowel a position already served
by a crank from below (double-up/clash).

---

## 6. Stirrups (keep them out of the slab)

- `StirrupBarType` + `StirrupSpacing` from Comments.
- **`StirrupOffsetTop` = thickness of the slab above** (`context.slab_above.thickness_in`).
  The column top usually coincides with the slab top, so the top ~slab-thickness of
  the column is inside the slab; this offset parks the highest tie at the slab soffit
  instead of inside the slab. Bottom offset: leave default (slab below sits beneath
  the column base).
- Hook: default `Stirrup/Tie - 135 deg.` (wraps the bar more fully; inherited by crossties).
  Use `Stirrup/Tie - 90 deg.` or `Stirrup/Tie Seismic - 135 deg.` if the project/Comments
  call for it — and only if it's in `available_rebar_hook_types`.
- Confinement (`ConfTop*`/`ConfBot*`): only if the schedule/Comments call for densified
  zones. Non-seismic default = none. (Schedule labels like `So 6" / Sm 10" / Lo 30"` map to
  `ConfTop/BotSpacing=6`, `ConfTop/BotZoneLength=30`, `StirrupSpacing=10`.)
- **Crossties** (interior шпильки): set `CrosstiesEnabled=true` when either (a) the Comments
  call out a second transverse item beyond the perimeter tie — e.g. `… + #4@10" TIE`, or a
  leg/set count like `2#4@10" STIRRUPS` (the `2` = outer tie **plus** a crosstie) — or (b) a
  face has interior longitudinal bars the outer tie alone can't laterally support (ACI 318-19
  §25.7.2.3, 6″ clear rule). Leave `CrosstiesAuto=true` — the engine places them per ACI from
  the bar layout, so you don't compute the pattern; the crosstie bar type/hook default to the
  outer tie's. Use `CrosstiesManual="x:i y:j"` only to force a specific pattern. Rectangular
  columns only.

---

## 7. Validation & strictness

- **Bar/hook names**: emit only names present in `available_rebar_bar_types` /
  `available_rebar_hook_types`. The plugin fails a column (with the available list) if
  a name is missing — don't invent `#20`-style aliases.
- **`ExpectedSection` / `ExpectedW` / `ExpectedD`** (or `ExpectedDia`): fill from the
  dump so the plugin cross-checks the row against the real column and flags a mismatch.
- **Mark uniqueness**: one row per Mark; duplicates → the later row wins (and a warning).

---

## 8. Output format

- Sparse CSV, one row per Mark, header row of the field names you use (any subset/order
  of the names in `assignments-csv-guide.md`).
- **UTF-8 with BOM** (Excel + Cyrillic comments render correctly).
- Lines starting with `#` are comments — **use them**: a short block per column
  explaining *why* each mode/dowel was chosen (footprint, which bars in/out, which
  face doweled). This is what lets the engineer verify your geometry calls quickly.
- No commas inside a cell (the selector lists use **spaces**).

---

## 9. Pre-handoff checklist

1. Every reinforced column has a row; Marks unique.
2. Bar counts reproduce the Comments (N = 2(nW+nD)−4).
3. Each splice: bars inside the upper footprint → Cranked; outside → BentToSlab.
4. Every bent-into-slab face has matching **starter dowels on the column above**
   (`DowelHost=Column`, `DowelPositions` = the complement, rotation-mapped). No
   position is both cranked-from-below **and** doweled.
5. Bends are **outward**; crank lower-bend offset clears the slab; penetration ≈ 30·d_b.
6. `StirrupOffsetTop` = slab-above thickness so ties don't enter the slab.
7. All bar/hook names exist in the dump's available lists.
8. Comment block explains each decision in plain language.

The **worked reference** that exercises every rule above is
[`samples/assignments-C1-stack.csv`](samples/assignments-C1-stack.csv) (the
C1.1→C1.4 stack: per-index split, outward bends, two-directional starter dowels,
stirrup offset). Read it alongside this guide.
