# WallReinforcement — Dev Plan

Revit 2025 plugin that batch-generates rebar for **cast-in-place (monolithic) reinforced-concrete walls**. Inspired by Be.Smart Wall Reinforcement, but the precast-specific parts (panel joints, lifting anchors, transport embeds) are out of scope.

References:
- Be.Smart Wall Reinforcement (precast) — feature map, naming, UX:
  https://docs.besmart.software/3d-modeling-and-design/precast-concrete/feature-descriptions/wall-reinforcement
- Be.Smart Smart Documentation — config-file/folder pattern we mirror:
  https://docs.besmart.software/2d-drafting-and-documentation/smart-documentation
- Revit 2025 API: https://www.revitapidocs.com/2025/

---

## 1. Specification

### 1.1 Target element
- Category: `OST_Walls` (`Autodesk.Revit.DB.Wall`).
- Structural usage: **Bearing / Shear** (monolithic). Non-structural partitions are filtered out.
- Geometry: straight walls and arc walls (single segment). Curtain walls, in-place walls, and stacked walls are **not supported** in Phase 1.
- Openings: hosted doors/windows + rectangular `Opening` cuts are detected and reinforced around.

### 1.2 Inputs
- One or more selected walls (or an "all walls in active view" mode).
- A **configuration file** (JSON) selected from a user-defined folder. The folder path is persisted in `Document.ProjectInformation` via `ExtensibleStorage` (same pattern as SmartViews).
- Active document must contain the rebar shapes and bar types the config references; if missing, the run aborts with a readable error list (no auto-loading of families in Phase 1).

### 1.3 Outputs
For each processed wall, the plugin creates:
- One `AreaReinforcement` element per reinforced face (external/internal) — produces the orthogonal mesh.
- `Rebar` elements for: opening trim bars, corner L-bars, T-junction bars, transverse ties (Phase 2+).
- All rebar grouped under one `TransactionGroup` per run so a single Undo reverts everything.

Naming: created elements get a `Comments` tag like `WR:{ConfigName}:{WallId}` so a later run can find and replace them.

### 1.4 Configurable parameters (JSON schema, v1)

```jsonc
{
  "name": "default-200mm-wall",
  "cover": {
    "exterior": 30,   // mm, distance from outer face
    "interior": 25,   // mm, distance from inner face
    "top": 30, "bottom": 30, "ends": 30
  },
  "faceMesh": {
    "exterior": {
      "vertical":   { "barType": "Ø12", "spacing": 200, "hookTop": "Std90", "hookBottom": null },
      "horizontal": { "barType": "Ø10", "spacing": 200, "hookEnds": null }
    },
    "interior":   { /* same shape */ }
  },
  "openings": {
    "trimBars":   { "barType": "Ø12", "extension": 500, "enabled": true },
    "diagonals":  { "barType": "Ø12", "length": 700, "angleDeg": 45, "enabled": true },
    "minWidthMm": 300   // openings smaller than this are skipped
  },
  "edges": {
    "top":    { "uBar": { "barType": "Ø10", "legLength": 250, "enabled": true } },
    "bottom": { "uBar": { "barType": "Ø10", "legLength": 250, "enabled": true } },
    "ends":   { "uBar": { "barType": "Ø10", "legLength": 250, "enabled": true } }
  },
  "ties": {
    "enabled": false,           // Phase 2
    "barType": "Ø8",
    "spacingX": 400, "spacingY": 400,
    "hookType": "135deg"
  },
  "corners": { "enabled": false /* Phase 2 */ },
  "tJunctions": { "enabled": false /* Phase 2 */ }
}
```

All length values are stored in **millimetres** in JSON and converted to internal feet at the API boundary (`UnitUtils.ConvertToInternalUnits`). Bar-type names are looked up by `Name` against `RebarBarType` in the active document.

### 1.5 Idempotency / re-runs
- Before placing new rebar, the engine deletes existing rebar whose `Comments` parameter starts with `WR:{ConfigName}:{WallId}`. This makes re-running the config on the same wall safe and predictable.
- A dry-run mode shows what would be created/deleted without committing.

### 1.6 Error handling
- Each wall is processed in its own sub-transaction inside the group. A failure on one wall is recorded and skipped; the rest proceed.
- The final dialog summarises: walls succeeded, walls skipped (with reason), elements created.

### 1.7 UI
- Single ribbon button on tab **Smart Tools** (shared with AutoNumbering), panel **Reinforcement**.
- WPF dialog (code-only, no XAML — same constraint as AutoNumbering for Linux-CI compilation):
  - Source selection: *Selected walls* / *Active view* / *Entire project*.
  - Config picker: dropdown populated from the configured folder + "Browse…" + "Edit JSON".
  - Mode toggle: *Dry run* / *Apply*.
  - Run button → progress bar → results summary.

---

## 2. Architecture

```
WallReinforcement/
├── App.cs                          IExternalApplication + ribbon
├── WallReinforcement.addin
├── WallReinforcement.csproj
├── Commands/
│   ├── WallReinforcementCommand.cs IExternalCommand entry point
│   └── WallSelectionFilter.cs      ISelectionFilter: structural walls only
├── Config/
│   ├── ReinforcementConfig.cs      POCO matching the JSON schema
│   ├── ConfigLoader.cs             JSON load/save + folder enumeration
│   └── FolderStorage.cs            ExtensibleStorage for the folder path
├── Engine/
│   ├── WallReinforcer.cs           Orchestrator: per-wall pipeline
│   ├── FaceMeshBuilder.cs          AreaReinforcement on each face
│   ├── OpeningTrimBuilder.cs       Trim + diagonal bars around openings
│   ├── EdgeBarBuilder.cs           U-bars at top/bottom/ends
│   ├── ExistingRebarCleaner.cs     Delete prior WR:* tagged rebar
│   └── UnitConv.cs                 mm <-> internal feet helpers
├── Domain/
│   ├── WallGeometry.cs             Faces, openings, edges extracted from Wall
│   └── RunResult.cs                Per-wall success/skip + counts
└── UI/
    ├── WallReinforcementDialog.cs  Code-only WPF window
    └── ResultsDialog.cs            Post-run summary
```

**Layering**: `Commands` → `Engine` → `Domain`; `Engine` consumes `Config`. `Domain` has no Revit-UI dependency. `UI` only talks to `Config` + `RunResult`.

**Transactions**:
- One `TransactionGroup` per run, `Assimilate()` on full success, `RollBack()` on user cancel.
- One `Transaction` per wall (so per-wall rollback works).
- Cleaner runs inside the same per-wall transaction as the new placement → atomic replace.

**Revit API building blocks** (Phase 1):
- `AreaReinforcement.Create(doc, host, curves, majorDir, areaReinforcementTypeId, rebarBarTypeId, rebarHookTypeId)` for face meshes.
- `HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior/Interior)` to get face references.
- `Wall.GetDependentElements(ElementCategoryFilter(OST_Doors|OST_Windows))` + wall solids for openings.
- `Rebar.CreateFromCurves` for trim/U-bars (Phase 1 minimal subset).

---

## 3. Roadmap

### Phase 0 — Scaffold (this PR)
- `.csproj`, `.addin`, `App.cs`, empty `Commands/Engine/Config/UI` folders with stub classes that compile.
- Ribbon button wired to a placeholder command that opens a "Hello" dialog.
- Same Linux-CI-compatible build setup as AutoNumbering (`EnableWindowsTargeting`, no XAML pages).

### Phase 1 — Face mesh MVP
- JSON config load/save + folder picker persisted via ExtensibleStorage.
- Selection of structural walls (single + multi).
- `FaceMeshBuilder`: orthogonal mesh on exterior + interior face, respecting cover.
- Idempotent re-run via `WR:*` Comments tag.
- WPF dialog: source, config picker, dry-run/apply, results summary.
- **Exit criteria**: select 5 walls of varying length/height → run config → 10 `AreaReinforcement` elements created, schedules look right, second run replaces cleanly.

### Phase 2 — Openings & edges ✅
- `OpeningTrimBuilder`: trim bars around hosted doors/windows and rectangular openings; diagonal corner bars.
- `EdgeBarBuilder`: U-bars at top/bottom/ends.
- Min-width filter for openings.

### Phase 3 — Junctions & ties ✅
- `TransverseTieBuilder`: stirrups across wall thickness on grid, skipped below `minThicknessMm`.
- `CornerBarBuilder` + `WallJunctions` detection: L-bars at wall-wall L-junctions, owner-by-min-ElementId to avoid double placement when both corner walls are in the run.
- `TJunctionBarBuilder`: lap bars from stem wall into through wall; alternating direction per height step.

### Phase 4 — Documentation hooks (separate plugin or shared lib)
- Hand-off to a future *SmartDocumentation* plugin for views/sheets/schedules of the rebar produced here. **Not built in this repo yet** — listed only so the JSON tag convention (`WR:*`) is designed to be queryable from elsewhere.

### Phase 5 — Niceties (deferred)
- Auto-load missing rebar families from a templates folder.
- Arc walls (sweep area-reinforcement along arc) — currently skipped.
- Bar-bending schedule export.

---

## 4. Out of scope (explicitly)

- Precast-only features: panel joints, lifting anchors, transport embeds, fabric sheets (mesh panels). The Be.Smart "Fabric Sheets / Fabric Area" page is not implemented — monolithic walls use loose bars assembled in `AreaReinforcement`.
- Stacked walls, curtain walls, in-place walls.
- Sloped walls and walls with non-vertical face normals.
- Cross-document references (linked models).
- Tag/dimension placement on views (belongs in the documentation plugin).

---

## 5. Open questions

1. **Bar-type lookup key** — `Name` is human-readable but easy to break by renames. Alternative: GUID stored in the config alongside name; name used as fallback for portability. → resolve before Phase 1 lands.
2. **Cover source of truth** — Revit walls have a built-in cover parameter (`WALL_ATTR_REBAR_COVER_EXTERIOR/INTERIOR_PARAM`). Should the config override it or read from it? Default plan: config wins, but a `"useWallCover": true` flag falls back to the wall's value.
3. **Hook orientation around openings** — vertical bars passing an opening get cut; do we add L-hooks at the cut ends or rely on the trim bar to provide continuity? Phase 2 decision.
4. **Schema versioning** — JSON files persist outside the repo. Add `"schemaVersion": 1` from day one and migrate forward.

---

## 6. Build / test

- Build: `dotnet build WallReinforcement/WallReinforcement.csproj -c Release` (works on Linux CI via `EnableWindowsTargeting`).
- Install: copy `WallReinforcement.addin` and the built DLL into `%AppData%\Autodesk\Revit\Addins\2025\`.
- Smoke test: a test `.rvt` with one straight wall + one wall-with-opening lives in `tests/fixtures/` (added in Phase 1).
