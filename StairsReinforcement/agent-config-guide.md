# Stair reinforcement — agent config guide

This is the operating manual for the **config-generation agent**: given a `StairsDump` JSON
(`schema_version ≥ 1`) plus the project's reinforcement brief, produce a sparse
`stairs-assignments.csv` that **Generate Stair Rebar** loads (From CSV → Run). It is a *decision
procedure, not a field reference* — field meanings live in `stairs-assignments-csv-guide.md`, the
input shape in `stairs-dump-schema.md`. This guide encodes the geometry reasoning that is easy to
get wrong.

**Norms:** ACI 318-19, Imperial (inches), non-seismic unless told otherwise. The plugin builds
geometry; **you (with the brief/engineer) choose bar sizes and spacing — the plugin sizes nothing
from loads.**

## §0 The flow
```
Revit ──[Export Stairs]──► stairs.json ─┐
                                         │   YOU = generation agent (json + brief)
                                         ▼
                              stairs-assignments.csv ──[Generate Stair Rebar]──► rebar
```
One row per **Mark**. Keep it sparse — emit a field only when it differs from the default.

## §1 Read the inputs
| datum | source in the dump | use |
|---|---|---|
| reinforcement brief | `stairs[].comments` (or a custom param — **confirm with the user**) | the primary spec |
| stair shape | `flights[]`, `landings[]`, `*.connects_flights` | which sets to place; one-way span direction |
| span direction | flight `local_basis.u_dir` (up-slope) | main bars run along the slope, dist across |
| section sizes | `waist_in`, `width_ft`, `thickness_in` | sanity-check the brief; fill `Expected*` |
| supports | flight `lower_support`/`upper_support`, landing `supports` | anchorage ends, starter host, knee folds |
| can it host rebar? | `rebar_host_ok` | **if false on a native stair, warn the user** before generating |
| allowed names | `available_rebar_bar_types`, `available_rebar_hook_types` | **strict** — use these names verbatim |

## §2 Flight main + distribution
Stairs span **one way, up the slope**. So:
- **Bottom main** (`FlightBotMain`) = the tension steel, running along `u_dir`. Size/spacing from
  the brief (e.g. `#5@8`). Anchor **both ends into the supports**: set `StartAnchor`/`EndAnchor` =
  `IntoSupport` (or `Hook90` at a thin slab/beam). This is the bar that must develop at each end.
- **Bottom distribution** (`FlightBotDist`) = secondary steel across the width (lighter, e.g.
  `#4@12`). It needs no end anchorage beyond cover.
- **Top** (`FlightTopMain`/`FlightTopDist`): set `FlightTopMode`. Use `OverSupports` (default) for
  negative moment at each end over a length `FlightTopSupportExtent` (≈ span/4, or per the brief);
  `Continuous` only if the brief calls for full-length top steel; `EndsOnly` for the upper
  re-entrant end alone.

## §3 Landings
A landing is a small two-way slab. Place `LandBotX`/`LandBotY` (the brief's `EW` mat) and, per
`LandingTopMode` (default `OverSupports`), `LandTopX`/`LandTopY`. Use `LandingMode=AreaSystem` only
if the team wants native AreaReinforcement (no per-bar length control); otherwise keep `Bars`.
A landing with two `connects_flights` carries both flights' reactions — keep its mat continuous.

## §4 Knee — the fold (read this carefully)
Where a flight meets a landing (`lower/upper_support.kind == "landing"`) the reinforcement turns a
corner. Two corners exist at a fold: a **soffit corner** (convex, bottom) and a **re-entrant
corner** (concave, top). The cardinal rule: **main tension steel must not be bent around the
re-entrant corner** — under tension it would spall the concrete off the inside of the bend.
- Default `KneeMode=LappedHairpin`: two U/hairpin bars lap across the fold so tension is carried by
  bars anchored into each component, never bent through the re-entrant corner. Safe and general.
- `CrossedAtReentrant`: diagonal bars across the re-entrant corner — use when the brief details it.
- `ContinuousBent`: one bar bent to follow the fold — acceptable on the **soffit** (compression)
  side only.
Set `KneeLeg` to the development length into each component (≈ lap length). Knee bars run across the
full `width_ft` at `KneeSpacing` (match the main spacing unless told otherwise).

## §5 Starters / dowels
If the stair starts off a slab/beam/wall/foundation (`lower_support.kind`), place starters so the
flight bottom steel is lapped into the support: `StartersEnabled=true`,
`StarterHost=<that kind>` (or `Auto`), `StarterForm=L` (embed leg + projection) for a slab/
foundation, `Straight` where a straight lap suffices. `StarterEmbed` into the support,
`StarterProjection` to lap the main bars (≈ lap length). Match `StarterSpacing` to `FlightBotMain`.

## §6 Steps
`StepsMode` is `None` by default (steps are usually unreinforced infill). Set `PerStepLBar` or
`NosingBar` only if the brief asks for nominal step steel; pick a small bar (`#3`) and a short
`StepsLeg`.

## §7 Strictness & validation
- Emit **only** names from the allow-lists; a bad name fails that one stair (with the available
  list) and the batch continues.
- Set `Expected*` so the plugin flags a model/schedule mismatch.
- One row per Mark; later duplicate wins (warning).
- If `rebar_host_ok=false` on a native stair, tell the user the bars may not host — the floor-
  modelled representation is the reliable fallback.

## §8 Output
Sparse CSV, one row per Mark, header = the columns you used. UTF-8 **with BOM**. Use `#` comment
lines to record the reasoning behind each stair's choices. Selectors (if any) use spaces, never
commas.

## §9 Pre-handoff checklist
1. Every `Mark` in scope has a row; names come only from the allow-lists.
2. Flight bottom main runs up-slope and is anchored at **both** ends.
3. `FlightTopMode` set wherever there is negative moment (over supports / re-entrant end).
4. Each landing has its bottom mat (and top per `LandingTopMode`); two-way mats where it bears two flights.
5. Every flight↔landing fold has a knee set; re-entrant corners use `LappedHairpin` or `CrossedAtReentrant`, **not** `ContinuousBent`.
6. Starters placed wherever a flight/landing bears on a slab/beam/wall/foundation.
7. `MaxBarLength`/lap set if any run is long; `Expected*` filled for validation.
8. Sparse — no column equals its default; `#` notes explain the non-obvious calls.

(Worked input: `example_stairs.json`. Worked output: `samples/` CSVs once present.)
