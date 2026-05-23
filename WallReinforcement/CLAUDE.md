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

## Phase 1 scope (current)
- Ribbon button on tab `Smart Tools`, panel `Reinforcement` (shares tab with AutoNumbering)
- JSON config load/save + folder picker
- Selection filter for structural walls (Bearing/Shear, straight or single-arc)
- Face mesh on exterior + interior face with per-face cover, spacing, bar type
- Idempotent re-run (delete existing `WR:*`-tagged rebar before placing new)
- WPF results dialog with per-wall success/skip summary

## Out of scope (Phase 1)
- Opening trim bars, U-bars (Phase 2)
- Corners, T-junctions, transverse ties (Phase 3)
- Stacked / curtain / in-place walls, sloped walls, linked models
- Fabric sheets (precast-only)

## References
- Full spec, architecture, roadmap: [wall-reinforcement-dev-plan.md](wall-reinforcement-dev-plan.md)
- Revit API docs: https://help.autodesk.com/view/RVT/2025/ENU/
- API class browser: https://www.revitapidocs.com/2025/
- Be.Smart Wall Reinforcement (precast): https://docs.besmart.software/3d-modeling-and-design/precast-concrete/feature-descriptions/wall-reinforcement
- Be.Smart Smart Documentation (config-folder pattern): https://docs.besmart.software/2d-drafting-and-documentation/smart-documentation
