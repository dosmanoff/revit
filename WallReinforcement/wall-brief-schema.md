# Wall reinforcement brief — JSON schema & agent guide

The **structured JSON brief** the agent produces from the wall dump + the project's reinforcement
task, and that **Wall Reinforcement** consumes (tick *Use per-wall JSON brief* in the dialog). One
entry per wall, matched by `mark` or `elementId`. Each entry is essentially a per-wall
[`ReinforcementConfig`](Config/ReinforcementConfig.cs) minus the document-level `name`: it reuses
the exact same section objects the single-config flow uses, so anything the dialog can set, the
brief can set per wall.

Authoritative definition: the POCO in [`Config/WallBrief.cs`](Config/WallBrief.cs). `schemaVersion: 1`,
camelCase keys, string enums. **Lengths** are a number (per `units`: `Metric`=mm, `Imperial`=in) or
a feet-inches string (`"1'-6\""`, always unambiguous).

```
units                    Metric | Imperial — how plain numeric lengths below are read
walls[]                  one entry per wall, matched by `mark` or `elementId`
 ├─ cleanExisting        delete this wall's prior WR:* rebar first (default true)
 ├─ cover       {exterior, interior, top, bottom, ends}
 ├─ faceMesh    {exterior{vertical{barType,spacing}, horizontal{…}}, interior{…}}
 ├─ openings    {enabled, barType, extension, minWidth, diagonals{…}}
 ├─ edges       {top{…}, bottom{…}, ends{…}}        each {enabled, barType, legLength, spacing}
 ├─ ties        {enabled, barType, spacingX, spacingY, minThickness}
 ├─ corners     {enabled, barType, lapLength, spacing}
 ├─ tJunctions  {enabled, barType, lapLength, spacing}
 └─ anchorage   {mode, fcPsi, fyPsi, epoxy, lightweight, adequateSpacing}
```

## Matching
Each `walls[]` entry sets `mark` and/or `elementId`. At run time every selected wall is matched to
an entry by **Mark** (case-insensitive) first, else by **elementId**. Unmatched walls are reported
and skipped. Use `elementId` for walls with no Mark (e.g. the dump's `element_id`).

## `faceMesh` — the two face mats (#1)
`exterior` / `interior`, each with `vertical` and `horizontal` `{barType, spacing}`. Placed as
native `AreaReinforcement` on the real side face (cover per `cover.exterior` / `cover.interior`).
Omit a face (`null`) to skip it.

## `openings` — trim around penetrations (#2)
`enabled`, `barType`, `extension` (how far each trim bar runs past the opening edge), `minWidth`
(skip openings narrower than this), and `diagonals` `{enabled, barType, length, angleDeg}`.
Openings are detected from the wall's real hosted inserts — you don't list them.

## `edges` — perimeter U-bars (#3)
`top`, `bottom`, `ends`, each `{enabled, barType, legLength, spacing}`. A U-bar wraps the edge
tying the two face mats together; `legLength` is the leg that returns into the wall.

## `ties` — transverse crossties / шпильки (#3)
`{enabled, barType, spacingX, spacingY, minThickness}`. A 135°-hooked crosstie across the wall
thickness on a `spacingX × spacingY` grid; skipped for walls thinner than `minThickness`.

## `corners` / `tJunctions` — continuity laps (#3)
`{enabled, barType, lapLength, spacing}`. L-bars lapping the two walls' mats at an L-corner, and
at a T-junction (placed from the stem wall). `lapLength` is each leg.

## `anchorage` — ACI 318-19 length governor (#6)
| Key | Values | Notes |
|---|---|---|
| `mode` | `Explicit` (default) \| `Aci` | `Explicit`: use the `legLength` / `lapLength` / `extension` typed above verbatim. `Aci`: derive them — development length ℓd (§25.4.2.3) for **edge legs** and **opening-trim extensions**, Class B tension lap ℓst = 1.3·ℓd (§25.5.2.1) for **corner / T laps** — per bar size. |
| `fcPsi`, `fyPsi` | psi | Concrete strength / steel yield (e.g. `5000` / `60000`). |
| `epoxy` | bool | ψe = 1.5 (capped ψt·ψe ≤ 1.7). |
| `lightweight` | bool | λ = 0.75. |
| `adequateSpacing` | bool (default true) | Bar spacing/cover meets §25.4.2.3 → smaller divisor. |

In `Aci` mode the explicit length is the **fallback** when the bar name is not ASTM (e.g. a metric
`"Ø12"`), so a metric config silently keeps its typed lengths. Use `Imperial` units + `#`-bars to
get ACI sizing.

## Agent procedure (summary)
1. Read the wall dump + the project task. Match each wall by `mark` / `element_id`.
2. **Faces**: set `faceMesh.exterior` / `interior` vertical + horizontal bars from the task.
3. **Openings**: enable trim if `hints.needs_opening_trim`; pick `barType` from the dump's list.
4. **Edges / ties / corners / tJunctions**: enable per `hints` (`has_corners`, `has_t_junctions`,
   `thick_enough_for_ties`) and the task.
5. **anchorage**: set `Aci` + project `fcPsi` / `fyPsi` to auto-size legs / laps; else `Explicit`.
6. Emit only valid bar/hook names (from the dump's available lists). Keep it explicit per wall.

## What the generator does automatically (you don't specify)
- **Opening detection** from the wall's real inserts — no rebar where there's no concrete.
- **Face mats** as native `AreaReinforcement` (spacing auto-fills the `NumberWithSpacing` layout).
- **Spaced builders** emit Revit rebar **sets** (one element per run), not N loose bars.
- **Tagging** `WR:{config}:{wallId}` so re-runs clean idempotently and **Wall Views** can isolate
  each wall's rebar.
