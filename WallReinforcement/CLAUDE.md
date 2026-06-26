# WallReinforcement Plugin — Revit 2025

## Project goal
Batch rebar generation plugin for Revit 2025 (.NET 8, C#).
Target: **cast-in-place (monolithic) reinforced-concrete walls**, NOT precast.
Reference product (precast equivalent): https://docs.besmart.software/3d-modeling-and-design/precast-concrete/feature-descriptions/wall-reinforcement

## Key decisions already made
- Config system: JSON files, stored in user-defined folder, folder path persisted via ExtensibleStorage (same as SmartViews)
- Primary primitive: `AreaReinforcement.Create` on each side face (`HostObjectUtils.GetSideFaces`)
- Secondary primitives: `Rebar.CreateFromCurves` for opening trim, U-bars at edges
- Naming/tagging: created rebar carries `Comments = "WR:{ConfigName}:{WallId}"` for idempotent re-runs
- Transactions: one `TransactionGroup` per run + one `Transaction` per wall
- UI: WPF dialog **built in code** (no XAML), same Linux-CI constraint as AutoNumbering
- Units: JSON stores millimetres; convert at API boundary via `UnitUtils.ConvertToInternalUnits`

## Implemented (at SlabReinforcement parity)
Ribbon tab `Smart Tools`, panel `Reinforcement`, four buttons: **Export Walls**, **Wall
Reinforcement**, **Wall Views**, **ACI Lengths**.
- **Reinforce:** structural-wall selection filter (Bearing/Shear/Combined basic walls); face mesh
  (native `AreaReinforcement`, exterior + interior, per-face cover/spacing/bar); opening trim +
  diagonals; perimeter U-bars (top/bottom/ends); transverse 135°-hooked crossties; L-corner and
  T-junction continuity laps. Spaced builders emit Revit rebar **sets**, not loose bars. Idempotent
  re-run via `WR:{config}:{wallId}` Comments tag. `WarningSwallower` rolls back a wall whose rebar
  is rejected (reported Failed, not Success).
- **Agent pipeline:** `Export Walls` → `WallDump` JSON (geometry/openings/junctions/cover/hints,
  `wall-dump-schema.md`); agent → per-wall `WallBrief` (`wall-brief-schema.md`); dialog *Use per-wall
  JSON brief* matches each wall by Mark/Id and reinforces from it.
- **ACI 318-19:** Anchorage tab + `ACI Lengths` reference calc. ACI mode sizes edge legs / opening
  extensions to ℓd and corner/T laps to Class B ℓst, per bar (non-ASTM bars keep the typed length).
- **Wall Views:** per-wall face elevations + thickness section + optional 3D cage + rebar schedule +
  sheet, each isolated to that wall's rebar; options dialog persisted via ExtensibleStorage.
- **Code split:** Revit-free `WallReinforcement.Geometry` (bar-layout math, feet-inches parser, ACI
  calculator) + `WallReinforcement.Tests` (xUnit, runs without Revit).

## Out of scope
- Stacked / curtain / in-place walls, sloped walls, linked models
- Fabric sheets (precast-only)

## References
- Full spec, architecture, roadmap: [wall-reinforcement-dev-plan.md](wall-reinforcement-dev-plan.md)
- Revit API docs: https://help.autodesk.com/view/RVT/2025/ENU/
- API class browser: https://www.revitapidocs.com/2025/
- Be.Smart Wall Reinforcement (precast): https://docs.besmart.software/3d-modeling-and-design/precast-concrete/feature-descriptions/wall-reinforcement
- Be.Smart Smart Documentation (config-folder pattern): https://docs.besmart.software/2d-drafting-and-documentation/smart-documentation
