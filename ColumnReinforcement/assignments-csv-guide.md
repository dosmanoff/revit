# assignments.csv — filling guide

Reference for **two audiences**:

1. **Human engineer** filling the CSV by hand, copying numbers off a structural schedule.
2. **Schedule Analyzer** (AI agent or other tool) generating the CSV automatically from a structural document.

Companion to [per-column-assignments-spec.md](per-column-assignments-spec.md) (the architectural design — read it once if you want the "why"). This file is the "how" — exhaustive field-by-field rules.

---

## 0. The 30-second version

A minimal CSV needs only `Mark`. Every other field is optional and falls back to the [POCO defaults](#defaults-at-a-glance) when blank.

```csv
Mark
C1.1
C1.2
C2.1
```

That parses, but every column gets the default config (16″×16″ assumption, `#8` × 3×3, `#4 @ 8″`, no dowels, no splices, no confinement). To actually customise: add columns for the parameters you want to override.

Real-world minimum-useful row:

```csv
Mark,LongBarType,LongBarsW,LongBarsD,StirrupBarType,StirrupSpacing,DowelsEnabled,DowelForm,SplicesEnabled,SpliceForm
C1.1,#11,4,4,#5,6,true,L,true,Cranked
```

That's a column with `#11` longitudinals 4×4, `#5` ties at 6″, L-form foundation dowels, and a Cranked splice to the column above.

---

## 1. File format basics

| Rule | Detail |
|---|---|
| **Extension** | `.csv` |
| **Encoding** | UTF-8 (with or without BOM) |
| **Delimiter** | Comma `,` |
| **Header** | **Required.** First non-blank, non-comment line. Field names matched case-insensitively. |
| **Comments** | Lines starting with `#` are ignored. Use freely to annotate sections. |
| **Blank lines** | Skipped. Use freely for readability. |
| **Quoting** | RFC 4180: wrap fields containing `,` or `"` or newlines in double quotes; escape inner `"` as `""`. |
| **Empty cells** | Mean "use POCO default". This is deliberate — write sparse files that only override what differs from defaults. |
| **Mark match** | Case-insensitive: `C1.1` and `c1.1` are the same row. Whitespace trimmed. |
| **Duplicate Marks** | Last row wins. A parse-issue note is shown under the validation table. |

### Lengths

Length cells accept two forms:

- **Number** — interpreted by the row's `Units` setting (`Imperial` = inches, `Metric` = mm). Example: `1.5`
- **Feet-inches string** — always parsed unambiguously, ignores `Units`. Examples: `1'-6"`, `1'-3 1/2"`, `6"`, `9 1/4"`

Mixing both styles in the same file is fine.

### Booleans

Accepted: `true` / `false`, `yes` / `no`, `Y` / `N`, `1` / `0`. Case-insensitive. Anything else logs a parse issue and falls back to the default.

### Enums

Field-specific enum values listed in the [reference table](#field-reference). Case-insensitive. Invalid value logs a parse issue.

---

## 2. Field reference

The first column of the table — **Field** — is the exact CSV header you write. Type column tells you what the parser expects. Default is what the plugin uses when the cell is empty. Notes explain when/how to set it.

### 2.1 Identity

| Field | Type | Default | Notes |
|---|---|---|---|
| `Mark` | string | — (**required**) | Must match the Revit column's `Mark` parameter. One row per Mark. Format up to you; the project convention is `C<id>.<level>` (e.g. `C1.1` = column #1 on level 1). |

### 2.2 Expected geometry (informational, validation only)

The engine **always** uses the column's real Revit geometry. These four fields drive the dialog's validation table — they catch mistakes where the schedule and the model disagree.

| Field | Type | Default | Notes |
|---|---|---|---|
| `ExpectedSection` | enum: `Rectangular` \| `Round` | inferred | If `ExpectedDia` is set and section is blank, defaults to `Round`. Otherwise `Rectangular`. |
| `ExpectedW` | number (in) | — | Width for Rectangular. |
| `ExpectedD` | number (in) | — | Depth for Rectangular. |
| `ExpectedDia` | number (in) | — | Diameter for Round. Ignored for Rectangular. |

### 2.3 Top-level

| Field | Type | Default | Notes |
|---|---|---|---|
| `Units` | enum: `Imperial` \| `Metric` | `Imperial` | Interpretation of plain-number lengths in this row. |
| `CleanExisting` | bool | `true` | When true, plugin deletes prior rebar it placed for this column+config before placing the new run. Leave `true` unless you want stacked re-runs. |

### 2.4 Cover

ACI 318 §20.5.1.3 minimums vary by exposure. Defaults are interior-column values.

| Field | Type | Default | Notes |
|---|---|---|---|
| `CoverSides` | length | `1.5` in | Cover from concrete face to outside of tie on the four vertical faces. |
| `CoverEnds` | length | `1.5` in | Cover at top and bottom (to ends of longitudinal bars). |

### 2.5 Longitudinal cage

Rectangular columns use `LongBarsW` and `LongBarsD`. Round columns use `LongBarsAround`. `LongCornersOnly` only affects rectangular and bypasses the counts.

| Field | Type | Default | Notes |
|---|---|---|---|
| `LongBarType` | string | `#8` | `RebarBarType.Name` in the document. Strict match — case-sensitive. ASTM A615 sizes: `#3` … `#11`, `#14`, `#18`. |
| `LongBarsW` | int | `3` | Bars along the column **width** (the LocalX face), including the two corner bars. Min 2. Rectangular only. |
| `LongBarsD` | int | `3` | Bars along the column **depth** (the LocalY face), including the two corner bars. Min 2. Rectangular only. |
| `LongBarsAround` | int | `8` | Number of bars equally spaced around the circumference. Round only. Min 3. |
| `LongCornersOnly` | bool | `false` | Rectangular only. If true, places exactly 4 corner bars and ignores `LongBarsW`/`LongBarsD`. |
| `LongHookTop` | string? | `null` | Optional `RebarHookType.Name` for the top end of longitudinals. `null` (empty cell) = no hook. |
| `LongHookBot` | string? | `null` | Same for the bottom end. |

### 2.6 Ties (transverse reinforcement)

| Field | Type | Default | Notes |
|---|---|---|---|
| `StirrupBarType` | string | `#4` | `RebarBarType.Name`. Use `#3` for #10 longitudinal and smaller; `#4` for #11 and larger (ACI 318 §25.7.2.1). |
| `StirrupSpacing` | length | `8` in | Center-to-center tie spacing along the column height. ACI 318 §25.7.2.1 minimum: `min(16·d_long, 48·d_tie, least column dim)`. |
| `StirrupHookType` | string? | `Stirrup/Tie - 90 deg.` | OOTB Revit tie-hook name. For seismic detailing, use `Stirrup/Tie - 135 deg.`. Empty cell = no hook (rare; ties almost always need hooks). |
| `StirrupOffsetTop` | length? | `null` | Distance from the top face of the column to the highest tie. `null` (empty) = use `CoverEnds`. Override when you need to skip the joint zone. |
| `StirrupOffsetBot` | length? | `null` | Same for the bottom face. |
| `StirrupRotate45` | bool | `false` | Phase 2+ feature. Rotates the rectangular tie 45° about the column axis (ACI §25.7.2.3 allowed cases). Currently a no-op flag — keep `false`. |

### 2.7 Confinement zones (densified ties)

Two independent zones (top, bottom). Each can be enabled separately. Inside the zone, `*Spacing` overrides the main `StirrupSpacing`. Zone length is either absolute (`*ZoneLength`) or a fraction of the column height (`*ZoneFraction`); when both are set, `ZoneLength` wins.

| Field | Type | Default | Notes |
|---|---|---|---|
| `ConfBotEnabled` | bool | `false` | Enable densified ties in the bottom zone. |
| `ConfBotSpacing` | length | `4` in | Densified spacing inside the bottom zone. |
| `ConfBotZoneLength` | length? | `null` | Absolute zone length from the bottom face. |
| `ConfBotZoneFraction` | number (0–1)? | `null` | Zone length as a fraction of column height. Typical SMRF value: `0.25` (i.e. `lo = h/4`). |
| `ConfTopEnabled` | bool | `false` | Same for the top zone. |
| `ConfTopSpacing` | length | `4` in | |
| `ConfTopZoneLength` | length? | `null` | |
| `ConfTopZoneFraction` | number (0–1)? | `null` | |

### 2.8 Foundation dowels (starter bars from slab below)

The engine searches for a slab directly below the column at the right elevation and plan position. If `DowelOnlyFoundation` is true (default), only `OST_StructuralFoundation` elements qualify; set to false to also search `OST_Floors`.

| Field | Type | Default | Notes |
|---|---|---|---|
| `DowelsEnabled` | bool | `false` | Master toggle. |
| `DowelForm` | enum: `Straight` \| `L` | `L` | `Straight` = single vertical bar. `L` = 90° bend at the bottom with a horizontal leg inside the slab. |
| `DowelBarType` | string | `#8` | Typically matches `LongBarType`. |
| `DowelExt` | length | `24` in | Vertical leg above the slab top — the lap-splice length with the column longitudinal. Compute via ACI calculator (`ℓst`, Class B). |
| `DowelEmbed` | length | `6` in | Vertical leg below the slab top — embedment into the slab. |
| `DowelLeg` | length | `9` in | L-form only: horizontal leg length inside the slab. Ignored for Straight. |
| `DowelOnlyFoundation` | bool | `true` | If false, regular `OST_Floors` slabs also qualify as the host. Set to false if your foundations are modelled as structural floors. |
| `DowelHookTop` | string? | `null` | Optional top-end hook. |
| `DowelHookBot` | string? | `null` | Optional bottom-end hook (rare with L-form, since the bend provides anchorage). |

### 2.9 Upper splices (continuation bars above the column)

Three forms cover the typical cases:

- **`Straight`** — single vertical bar extending past the column top into the column above. Default form. Use for intermediate floors with same-size columns above.
- **`Bent`** — vertical leg up to just below the slab top above, then 90° bend with a horizontal leg anchoring inside the slab. **Requires a slab above.** Use at roof level when there's no column continuing up.
- **`Cranked`** — three-segment Z-shape: vertical inside lower column → diagonal offsetting inward → vertical inside upper column. **Use when the upper column is smaller (or offset) than the lower.**

| Field | Type | Default | Notes |
|---|---|---|---|
| `SplicesEnabled` | bool | `false` | Master toggle. |
| `SpliceForm` | enum: `Straight` \| `Bent` \| `Cranked` | `Straight` | See above. |
| `SpliceBarType` | string | `#8` | Typically matches `LongBarType` (column longitudinal). |
| `SpliceLap` | length | `24` in | Length INSIDE the lower column where the splice laps with the column longitudinal. ACI Class B = `1.3·ℓd`. |
| `SpliceExt` | length | `24` in | Straight: extension above column top (or above slab top if a slab is above and `SpliceIgnoreSlabAbove=false`). Cranked: vertical leg INSIDE the upper column (lap with the upper longitudinal). Bent: ignored (uses `SpliceBentLeg` instead). |
| `SpliceBentLeg` | length | `12` in | Bent only: horizontal leg length inside the slab above. |
| `SpliceUpperInset` | length | `2` in | Cranked only: how much smaller the upper cage is on each side. E.g. 2 = upper column is 4″ narrower in both width and depth. |
| `SpliceCrankedSlope` | number | `6` | Cranked only: vertical/horizontal slope of the diagonal segment. ACI 318 §10.7.4.1 caps at 1:6 — keep `6` unless you know what you're doing. |
| `SpliceLowerBendOffset` | length | `6` in | Cranked only: distance from the column top down to the FIRST bend (top of lower vertical leg). |
| `SpliceIgnoreSlabAbove` | bool | `false` | Straight only: when true, measure `SpliceExt` from the column top regardless of a slab above. Useful when the upper joint isn't modelled. |
| `SpliceHookTop` | string? | `null` | Optional top-end hook (rare). |
| `SpliceHookBot` | string? | `null` | Optional bottom-end hook (rare). |

---

## 3. Defaults at a glance

If you write only `Mark` and run, every column gets this implicit config:

| Setting | Default |
|---|---|
| Section | inferred from Revit geometry (rect or round) |
| Units | `Imperial` |
| Cover sides / ends | `1.5″` / `1.5″` |
| Longitudinal | `#8`, 3×3 (or 8-around for round), no hooks |
| Ties | `#4 @ 8″`, hook `Stirrup/Tie - 90 deg.`, no offsets, no confinement |
| Dowels | disabled |
| Splices | disabled |
| CleanExisting | `true` |

---

## 4. Worked examples

### 4.1 Interior column at an intermediate floor

Standard case: column continues to a same-size column above, sits on a floor below (not a foundation), wants confinement zones near the joints, no dowels (the column below already has its own splices).

```csv
Mark,LongBarType,LongBarsW,LongBarsD,StirrupBarType,StirrupSpacing,ConfBotEnabled,ConfBotSpacing,ConfBotZoneFraction,ConfTopEnabled,ConfTopSpacing,ConfTopZoneFraction,SplicesEnabled,SpliceForm,SpliceLap,SpliceExt
C2.1,#10,4,4,#4,6,true,3,0.25,true,3,0.25,true,Straight,30,30
```

### 4.2 Ground-level column on a foundation, smaller column above

Foundation dowels (L-form) + Cranked splice to the smaller upper column. This is the most demanding row — every section enabled.

```csv
Mark,LongBarType,LongBarsW,LongBarsD,StirrupBarType,StirrupSpacing,ConfBotEnabled,ConfBotSpacing,ConfBotZoneFraction,DowelsEnabled,DowelForm,DowelBarType,DowelExt,DowelEmbed,DowelLeg,SplicesEnabled,SpliceForm,SpliceBarType,SpliceLap,SpliceExt,SpliceUpperInset
C1.1,#11,4,4,#5,4,true,3,0.25,true,L,#11,36,8,12,true,Cranked,#11,36,30,2
```

Translation:
- `#11` 4×4 cage with `#5` ties at 4″ spacing
- Confinement zone in bottom 1/4 of height
- L-form `#11` dowels: 36″ above slab (lap with column), 8″ embedded, 12″ horizontal leg
- Cranked splice to upper 16×16 column (2″ inset per side): 36″ lap inside lower, 30″ lap inside upper

### 4.3 Roof-level column with bent splice into the slab

No upper column — splice bends into the roof slab.

```csv
Mark,LongBarType,LongBarsW,LongBarsD,StirrupBarType,StirrupSpacing,SplicesEnabled,SpliceForm,SpliceLap,SpliceBentLeg
C2.3,#8,3,3,#4,8,true,Bent,24,12
```

### 4.4 Round column with foundation dowels

```csv
Mark,ExpectedSection,ExpectedDia,LongBarType,LongBarsAround,StirrupBarType,StirrupSpacing,DowelsEnabled,DowelForm,DowelExt,DowelEmbed,DowelLeg
C3.1,Round,24,#9,8,#4,8,true,L,30,6,9
```

### 4.5 Minimal-cage small interior column

Just corner bars, smallest legal tie, no extras.

```csv
Mark,LongBarType,LongCornersOnly,StirrupBarType,StirrupSpacing
C4.1,#6,true,#3,6
```

---

## 5. What the plugin does with your CSV

Run the plugin, click `From CSV`, browse to your file. The dialog shows a validation table, one row per Mark seen in either CSV or the current Revit selection. Status column flags issues:

| Status | Meaning |
|---|---|
| `OK` | Mark in both, sizes agree. Will be processed. |
| `⚠ size mismatch` | Both have the Mark, but `ExpectedW/D/Dia` doesn't match the actual Revit geometry. Plugin proceeds using the **real** Revit size — informational only. |
| `⚠ section: CSV=X, Revit=Y` | Section type disagrees (e.g. CSV says Round, Revit has Rectangular). Plugin proceeds with Revit's section. May fail downstream if e.g. `LongBarsAround` is the only thing set for what's actually a rectangular column. |
| `⚠ no CSV assignment` | Mark exists in Revit selection but not in CSV. Skipped — unless the "Fall back to selected JSON config" checkbox is on, then it gets the JSON config. |
| `⚠ not in selection` | Mark exists in CSV but not in the current Revit selection. No-op (the CSV is bigger than the selection — that's fine; just means you're only processing a subset right now). |
| `⚠ no Mark on column` | A selected Revit column has an empty `Mark` parameter. Skipped — fill in the Mark parameter, or rely on fallback. |
| `⚠ duplicate Mark in selection` | Two selected Revit columns share the same Mark. Only the first one gets the config. **Marks must be unique per column instance.** |

All warnings are non-blocking. The Run button always runs. Skipped columns get a clear `Reason` in the post-run summary.

---

## 6. For Schedule Analyzer (AI agent) — extraction rules

This section is the contract the analyzer is meant to satisfy. If you're an AI agent reading a structural document and producing this CSV, follow these mappings.

### 6.1 Mark format

- One row per **column instance**, not per column type.
- Mark format: project convention. Default scheme: `C<id>.<level>` (e.g. `C1.1` = column #1 on level 1). Use the convention the existing Revit model uses — read the `Mark` parameter off any one column and match the format.

### 6.2 Geometry → `ExpectedSection` / `ExpectedW` / `ExpectedD` / `ExpectedDia`

- Square or rectangular section → `ExpectedSection=Rectangular`, fill `ExpectedW` and `ExpectedD` in inches.
- Circular section → `ExpectedSection=Round`, fill `ExpectedDia` in inches. Leave `ExpectedW`/`ExpectedD` blank.
- If geometry is not in the schedule, leave all four blank — the dialog won't size-validate but everything else still works.

### 6.3 Reinforcement → `Long*` and `Stirrup*`

Source: column schedule rows of the form **"8 #11, ties #4 @ 6 o.c."** or similar.

- **Total bar count → cage layout**: For a rectangular cage with N bars total, prefer the symmetric arrangement. Map common totals: `4 → cornersOnly=true`; `6 → 3×3`; `8 → 3×3 corners + sides, set BarsAlongWidth=BarsAlongDepth=3`; `12 → 4×4`; `16 → 5×5` (etc.). For asymmetric counts (e.g. `10`), use `BarsAlongWidth=3, BarsAlongDepth=4` for a 12-bar perimeter — actual logic preserves perimeter only, so total = `2*(W-2) + 2*(D-2) + 4` corners. Validate this matches the schedule total.
- **Round columns**: `BarsAround = total bar count`.
- **Bar size** → `LongBarType` as `#N` (US Imperial bar designation, ASTM A615).
- **Tie size** → `StirrupBarType` as `#N`.
- **Tie spacing** → `StirrupSpacing` in inches (or feet-inches string). The "@ X o.c." value.

### 6.4 Confinement → `Conf*`

Look for notes like:
- **"Densely spaced ties at top and bottom of column"** → both `ConfBotEnabled=true` and `ConfTopEnabled=true`.
- **"l₀ = h/4 (or h/6)"** → `ConfBotZoneFraction = 0.25` (or `0.167`).
- **"l₀ = 36 inches"** → `ConfBotZoneLength = 36`.
- **Tighter spacing in confinement zone**, e.g. "#4 @ 3 o.c."** → `ConfBotSpacing = 3`, `ConfTopSpacing = 3`.

If the schedule says nothing about confinement, leave all `Conf*` fields blank.

### 6.5 Dowels → `Dowel*`

Set when the column sits on a foundation or transfers from a slab below.

- **"Dowels: 8 #11 × 4'-0″ extending into column"** → `DowelsEnabled=true`, `DowelBarType=#11`, `DowelExt=48` (or `4'-0"`). Bar count usually matches the column longitudinal count — no separate field, the plugin places one dowel per longitudinal.
- **"L-shaped, 12" leg into footing"** → `DowelForm=L`, `DowelLeg=12`, `DowelEmbed=` slab thickness − slab top cover (or what the schedule says).
- **"Straight dowels"** → `DowelForm=Straight`.

If the foundation in Revit is modelled as `OST_Floors` instead of `OST_StructuralFoundation`, set `DowelOnlyFoundation=false`.

### 6.6 Splices → `Splice*`

Set on every column row except the topmost (where the column terminates with `Bent` or no splice).

- **Same-size column above** → `SpliceForm=Straight`, `SpliceLap` and `SpliceExt` = ACI Class B lap length (typically `1.3·ℓd`).
- **Smaller upper column** → `SpliceForm=Cranked`. `SpliceUpperInset` = `(lower_W − upper_W) / 2`. Default `SpliceCrankedSlope=6` (1:6 per ACI 318 §10.7.4.1). Leave `SpliceLowerBendOffset=6` unless the schedule specifies.
- **Roof level, no column above** → `SpliceForm=Bent`, `SpliceBentLeg` from schedule (typically 12 in).

If splices are detailed separately in a "splice schedule" rather than the column schedule, the analyzer needs to cross-reference. If the splice schedule isn't available, default to `Straight` for intermediate levels and `Bent` for the top level.

### 6.7 Validation expectations

Before writing the CSV, the analyzer should:

1. Confirm every `Mark` it emits is unique in the file.
2. Confirm the format matches the Revit model's `Mark` convention (read one column to check).
3. Confirm bar designations are in `{#3, #4, #5, #6, #7, #8, #9, #10, #11, #14, #18}`.
4. Confirm enum values are exactly: `Imperial`/`Metric`, `Rectangular`/`Round`, `Straight`/`L` (dowels), `Straight`/`Bent`/`Cranked` (splices). Wrong case is OK (parser is case-insensitive); typos are not.
5. For Cranked splices: `SpliceUpperInset > 0` and `(geom.height − SpliceLap)` should be greater than `SpliceLowerBendOffset` (the lower bend must sit above the lap zone). The plugin will error out per-column if not, but better to catch it upstream.

### 6.8 What to leave blank

When in doubt, leave the cell blank. The plugin's defaults are sensible for the typical interior US column. Filling in everything everywhere bloats the CSV and produces brittle output — the schedule analyzer should write **only what's specified in the schedule**.

---

## 7. Round-trip

The plugin can also write CSVs back — `AssignmentCsv.Format(table)` serialises a loaded `AssignmentTable` to a canonical CSV with every field present. Used for:

- Exporting a project's current per-column configs to a CSV
- Re-canonicalising a hand-written CSV (fill in default values, normalise column order)
- Testing the loader (parse → format → parse → assert equal)

Not exposed in the dialog yet — call from code if you need it. Will land as an "Export to CSV" command in a future PR.
