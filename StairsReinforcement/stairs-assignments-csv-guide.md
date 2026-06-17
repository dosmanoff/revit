# stairs-assignments.csv — field reference

One row per stair **Mark**. **Sparse**: emit a column only when its value differs from the config
default — every omitted/empty cell keeps the default in `samples/default.json`. UTF-8 **with BOM**.
Lines starting with `#` are comments (use them to record *why* a choice was made). Header is the
set of columns you actually use, in any order; lookups are case-insensitive. Duplicate Mark ⇒ the
later row wins (with a warning).

Lengths accept a plain number (interpreted per `Units` — inches Imperial / mm Metric) **or** a
feet-inches string like `2'-0"`, `1'-3 1/2"`, `3"`. Bools: `true/yes/y/1` or `false/no/n/0`.

## Per-set columns (the `BarSetSpec` shape)
Eight bar **sets** each take the same suffixes. Column name = **prefix + suffix**.

**Prefixes:** `FlightBotMain`, `FlightBotDist`, `FlightTopMain`, `FlightTopDist`,
`LandBotX`, `LandBotY`, `LandTopX`, `LandTopY`.

| suffix | values | meaning |
|---|---|---|
| `BarType` | a name from `available_rebar_bar_types` | bar size (strict) |
| `Enabled` | bool | place this set (default true) |
| `Spacing` | length | centre-to-centre spacing (when not using Count) |
| `Count` | int | explicit bar count (sets SpacingMode=Count) |
| `Cover` | length | side/face cover override for this set (else uses CoverTop/Bottom/Side) |
| `StartAnchor` / `EndAnchor` | `Straight\|Hook90\|Hook180\|IntoSupport\|BendUp\|BendDown` | end treatment |
| `StartAnchorLen` / `EndAnchorLen` | length | straight extension / development length at that end |
| `StartHook` / `EndHook` | a name from `available_rebar_hook_types` | hook type for a hooked end |

Example: `FlightBotMainBarType=#5  FlightBotMainSpacing=8  FlightBotMainStartAnchor=IntoSupport`.

## Standalone columns
| column | values / default | meaning |
|---|---|---|
| `Mark` | *required* | stair Mark to match the dump |
| `Units` | `Imperial`(d)/`Metric` | how plain numbers in this row are read |
| `CleanExisting` | bool (true) | delete this config's prior bars before placing |
| `CoverTop`,`CoverBottom`,`CoverSide` | length (1.5″) | default covers |
| **Flight top** | | |
| `FlightTopMode` | `None\|Continuous\|OverSupports`(d)`\|EndsOnly` | where the flight top layer goes |
| `FlightTopSupportExtent` | length (3'-0″) | top-bar length from each support (OverSupports/EndsOnly) |
| **Steps** | | |
| `StepsMode` | `None`(d)`\|PerStepLBar\|NosingBar` | per-step reinforcement |
| `StepsBarType` | bar name (#3) | step bar |
| `StepsLeg` | length (6″) | L-leg / nosing embedment |
| **Landing** | | |
| `LandingMode` | `Bars`(d)`\|AreaSystem` | discrete lapped bars vs native AreaReinforcement |
| `LandingTopMode` | `None\|Continuous\|OverSupports`(d)`\|EndsOnly` | landing top mat extent |
| `LandingTopSupportExtent` | length (3'-0″) | landing top-mat length from support |
| **Knee (fold)** | | |
| `KneeEnabled` | bool (true) | bars around flight↔landing / flight↔flight folds |
| `KneeMode` | `ContinuousBent\|LappedHairpin`(d)`\|CrossedAtReentrant` | fold detail |
| `KneeBarType` | bar name (#5) | knee bar |
| `KneeSpacing` / `KneeCount` | length / int | quantity across width |
| `KneeLeg` | length (2'-0″) | leg into each component from the fold |
| **Starters / dowels** | | |
| `StartersEnabled` | bool (true) | dowels from a support into the stair |
| `StarterHost` | `Auto`(d)`\|SlabBelow\|Beam\|Wall\|Foundation\|None` | what they anchor into |
| `StarterForm` | `Straight\|L`(d) | dowel shape |
| `StarterBarType` | bar name (#5) | dowel |
| `StarterSpacing` / `StarterCount` | length / int | quantity across width |
| `StarterEmbed` | length (1'-6″) | embedment into the support |
| `StarterProjection` | length (2'-0″) | projection (lap with the main bars) |
| **Lengths / lap** | | |
| `MaxBarLength` | length (40'-0″) | split longer runs and lap |
| `LapMode` | `Factor`(d)`\|Length` | lap by factor·dᵦ or absolute |
| `LapLength` | length (2'-0″) | absolute lap (LapMode=Length) |
| `LapFactor` | number (40) | ACI Class-B ≈ 40·dᵦ (LapMode=Factor) |
| `LapStagger` | bool (true) | stagger splice locations |
| **Validators (optional)** | | |
| `ExpectedWaist` | length | warn if the live waist differs |
| `ExpectedWidth`,`ExpectedRise` | length | warn on geometry mismatch |

## Minimal useful row
```
# ST-1: monolithic U-stair, B main #5@8 anchored both ends, dist #4@12, top over supports.
Mark,FlightBotMainBarType,FlightBotMainSpacing,FlightBotMainStartAnchor,FlightBotMainEndAnchor,FlightBotDistBarType,FlightBotDistSpacing,FlightTopMode,FlightTopMainBarType,FlightTopMainSpacing,LandBotXBarType,LandBotXSpacing,LandBotYBarType,LandBotYSpacing,StartersEnabled,StarterHost
ST-1,#5,8,IntoSupport,IntoSupport,#4,12,OverSupports,#5,8,#5,8,#5,8,true,SlabBelow
```
