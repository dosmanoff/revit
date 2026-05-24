# WallReinforcement

Revit 2025 plugin for batch-placing rebar on **cast-in-place (monolithic) reinforced-concrete walls**.

See [`wall-reinforcement-dev-plan.md`](wall-reinforcement-dev-plan.md) for the full spec, architecture, and roadmap. See [`CLAUDE.md`](CLAUDE.md) for the short context summary.

## Install

```powershell
dotnet build WallReinforcement\WallReinforcement.csproj -c Release
```

Copy two files into the per-user Revit add-ins folder:

```text
WallReinforcement.dll   →  %AppData%\Autodesk\Revit\Addins\2025\
WallReinforcement.addin →  %AppData%\Autodesk\Revit\Addins\2025\
```

Restart Revit. A new tab **Smart Tools** → panel **Reinforcement** → button **Wall Reinforcement** appears.

## Configure

The plugin is driven by JSON files living in a user-chosen folder. The folder path is stored on `ProjectInformation` via ExtensibleStorage, so it travels with the `.rvt`. Configure it the first time you run the tool via **Browse…**.

Two ready-to-use samples ship in [`samples/`](samples/):

| File | What it does |
|---|---|
| [`samples/minimal.json`](samples/minimal.json) | Only face-mesh on both faces (replicates Phase 1) |
| [`samples/full.json`](samples/full.json)       | All builders enabled: openings, edges, ties, corners, T-junctions |

All distances are in **millimetres**; `barType` values are looked up by `Name` against `RebarBarType` in the active document — the families must already be loaded. JSON keys are documented inline in [`wall-reinforcement-dev-plan.md`](wall-reinforcement-dev-plan.md#14-configurable-parameters-json-schema-v1).

## Smoke-test recipe

1. Open a project that already has the rebar families referenced by the config loaded (or modify the sample to use bar types you do have).
2. Draw a structural straight wall, ~3 m long, 3 m tall, thickness ≥ 250 mm (so ties fire). Place a door or window if you want to see the opening trim work.
3. Click **Wall Reinforcement**, pick the wall, **Browse…** to `WallReinforcement/samples/`, pick `full.json`, leave **Dry run** unchecked, hit **Run**.
4. Expected output: 2 `AreaReinforcement` elements (one per face), opening trim + diagonals, U-bars along three edges, transverse ties on a 400 × 400 grid.
5. Re-run with the same config → previously-placed bars (tagged `WR:full-300mm-monolithic:<wallId>` in the `Comments` parameter) get deleted before new ones land. Idempotent.
6. Toggle **Dry run** → totals show in the summary but nothing commits.

## Adding new builders

Each builder lives in `Engine/` and follows this shape:

```csharp
public class XxxBuilder
{
    private readonly Document _doc;
    public XxxBuilder(Document doc) => _doc = doc;

    public int Build(WallAxes axes, ReinforcementConfig cfg, string tag) { ... }
}
```

Shared helpers (`LookupBarType`, `EvenlySpaced`, `Create`) live in [`Engine/RebarFactory.cs`](Engine/RebarFactory.cs). Use them. Wire your new builder into [`Engine/WallReinforcer.cs`](Engine/WallReinforcer.cs) and tag every placed rebar with the supplied `tag` so re-runs clean up correctly.
