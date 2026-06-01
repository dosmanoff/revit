# `slab-assignments.csv` — filling guide

Reference for the two audiences who fill it: a **human engineer** copying a slab
reinforcement schedule, and the **agent** generating it from a slab dump. Companion to
[`agent-config-guide.md`](agent-config-guide.md) (the decision procedure) and
[`slab-dump-schema.md`](slab-dump-schema.md) (the input).

One row per slab **`Mark`**. Empty cell = plugin default — keep the file **sparse**,
emit only what differs. A second file, `slab-zones.csv`, carries top strengthening zones.

---

## 0. The 30-second version

Minimal CSV needs only `Mark` (every other field defaults):

```csv
Mark
S-101
S-102
```

Real minimum-useful row — bottom + top mesh, a max bar length, and edge U-bars:

```csv
Mark,BottomXBarType,BottomXSpacing,BottomYBarType,BottomYSpacing,TopMode,MaxBarLength,EdgeUBarsEnabled
S-101,#5,12,#5,12,OverSupports,40'-0",true
```

---

## 1. File format

| Rule | Detail |
|---|---|
| Encoding | UTF-8 (BOM ok). |
| Delimiter | Comma. Header row required; field names matched case-insensitively. |
| Comments | Lines starting with `#` are ignored — use them to explain each slab. |
| Empty cells | "use the default". |
| `Mark` match | Case-insensitive, trimmed. Duplicate Marks → last row wins (warning). |
| Lengths | A plain **number** is interpreted by `Units` (`Imperial`=inches, `Metric`=mm); a **feet-inches string** (`40'-0"`, `1'-6"`, `9 1/4"`) is unambiguous and overrides `Units`. |
| Booleans | `true/false`, `yes/no`, `Y/N`, `1/0`. |
| Selectors | Space-separated, never commas (e.g. `EdgeUBarSelector=0 2`). |
| Bar/hook names | **Strict** — only names from the dump's `available_rebar_*` lists. |

---

## 2. Field reference

### 2.1 Identity & top-level
| Field | Type | Default | Notes |
|---|---|---|---|
| `Mark` | string | — (**required**) | Matches the floor's `Mark`. |
| `ExpectedThickness` | length (in) | — | Validation only; flags schedule/model mismatch. |
| `Units` | `Imperial`\|`Metric` | `Imperial` | Interpretation of plain-number lengths. |
| `CleanExisting` | bool | `true` | Delete this config's prior `SR:` rebar before placing. |
| `FieldMode` | `Bars`\|`AreaSystem` | `Bars` | **Bars** = individual rebar, split at `MaxBarLength` + lapped (full control). **AreaSystem** = native `AreaReinforcement` (Revit lays out the run; no per-bar length control). |

### 2.2 Cover
| Field | Type | Default | Notes |
|---|---|---|---|
| `CoverTop` | length | `1.5"` | Face → outer top bar. |
| `CoverBottom` | length | `1.5"` | Face → outer bottom bar. |
| `CoverSide` | length | `1.5"` | Edge → bar end. |

### 2.3 Bottom mesh (always placed unless bar type blank)
| Field | Type | Default | Notes |
|---|---|---|---|
| `BottomXBarType` | string | `#5` | Bars along local **X**, near the bottom face. |
| `BottomXSpacing` | length | `12"` | Center-to-center. |
| `BottomYBarType` | string | `#5` | Bars along local **Y**. |
| `BottomYSpacing` | length | `12"` | |

### 2.4 Top mesh
| Field | Type | Default | Notes |
|---|---|---|---|
| `TopMode` | `None`\|`Continuous`\|`OverSupports`\|`Edges` | `None` | `Continuous` = full top mat; `OverSupports` = top bars only in support strips (see `slab-zones.csv`); `Edges` = top bars only along supported edges. |
| `TopXBarType` / `TopXSpacing` | string / length | `#5` / `12"` | Used when `TopMode=Continuous`. |
| `TopYBarType` / `TopYSpacing` | string / length | `#5` / `12"` | |

### 2.5 Length & lap (the `MaxBarLength` requirement)
| Field | Type | Default | Notes |
|---|---|---|---|
| `MaxBarLength` | length | `40'-0"` | Runs longer than this are split into segments. |
| `LapMode` | `Length`\|`Factor` | `Factor` | How the lap is computed. |
| `LapLength` | length | `2'-0"` | Used when `LapMode=Length`. |
| `LapFactor` | number | `40` | Used when `LapMode=Factor`: lap = `LapFactor × d_b` (ACI Class B ≈ 40·d_b for typical slab bars). |
| `LapStagger` | bool | `true` | Offset adjacent bars' splices by half a bay so laps don't line up. |

### 2.6 Edge anchorage (field bars into supports)
| Field | Type | Default | Notes |
|---|---|---|---|
| `EdgeAnchorMode` | `Straight`\|`Hook90`\|`Hook180`\|`IntoSupport` | `Straight` | How field bars terminate at a supported edge. `IntoSupport` extends into the beam/wall by `EdgeAnchorLen`. |
| `EdgeAnchorLen` | length | `2'-0"` | Anchorage/extension length. |

### 2.7 Edge U-bars (П-образные по торцам)
| Field | Type | Default | Notes |
|---|---|---|---|
| `EdgeUBarsEnabled` | bool | `false` | Place closing U-bars wrapping the slab edge top-to-bottom. |
| `EdgeUBarType` | string | `#5` | |
| `EdgeUBarSpacing` | length | `12"` | Along the edge. |
| `EdgeUBarLeg` | length | `1'-0"` | Leg length into the slab. |
| `EdgeUBarSelector` | `free`\|`all`\|indices | `free` | Which boundary edges (default: the dump's `free_edge_indices`). |

### 2.8 Opening trim (по торцам отверстий)
| Field | Type | Default | Notes |
|---|---|---|---|
| `OpeningTrimEnabled` | bool | `false` | Reinforce opening edges. |
| `OpeningTrimBarType` | string | `#5` | Extra straight bars along each opening edge (top & bottom). |
| `OpeningExtraEachSide` | int | `2` | Extra bars per side. |
| `OpeningUBars` | bool | `true` | U-bars wrapping the opening edge. |
| `OpeningDiagonals` | bool | `true` | Diagonal bars at opening corners. |
| `OpeningDiagBarType` | string | `#5` | |
| `OpeningSelector` | `all`\|indices | `all` | Opening ids (default: the dump's `openings_need_trim`). |

---

## 3. `slab-zones.csv` — top strengthening zones

Strengthening over supports is spatial, so it lives in its own file. One row per zone.

| Field | Type | Notes |
|---|---|---|
| `SlabMark` | string | Which slab (matches `Mark`). |
| `ZoneName` | string | Free label, e.g. `C-3 strip`. |
| `Face` | `Top`\|`Bottom` | Which mat. |
| `Direction` | `X`\|`Y` | Bar direction in the slab's local frame. |
| `BarType` | string | Strict bar name. |
| `Spacing` | length | Center-to-center. |
| `Shape` | string | One of: `SupportMark:<mark>:<stripWidth>` (band centred on the support), `BBox:x1 y1 x2 y2` (feet, world XY), `Polygon:x y x y …`. |
| `Extent` | length | How far the bars run past the support face (each side) when `Shape=SupportMark`. |

Example:

```csv
SlabMark,ZoneName,Face,Direction,BarType,Spacing,Shape,Extent
S-101,C-3 strip X,Top,X,#6,8,SupportMark:C-3:5'-0",6'-0"
S-101,C-3 strip Y,Top,Y,#6,8,SupportMark:C-3:5'-0",6'-0"
```

---

## 4. Defaults at a glance

`Units=Imperial`, `FieldMode=Bars`, cover `1.5"`, bottom `#5@12"` EW, `TopMode=None`,
`MaxBarLength=40'-0"`, `LapMode=Factor LapFactor=40 LapStagger=true`,
`EdgeAnchorMode=Straight EdgeAnchorLen=2'-0"`, edge U-bars off, opening trim off.
