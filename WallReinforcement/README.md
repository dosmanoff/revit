# WallReinforcement

Revit 2025 plugin for batch-placing rebar on **cast-in-place (monolithic) reinforced-concrete walls**.

See [`wall-reinforcement-dev-plan.md`](wall-reinforcement-dev-plan.md) for the full spec, architecture, and roadmap. See [`CLAUDE.md`](CLAUDE.md) for the short context summary.

## Install

```powershell
dotnet build WallReinforcement\WallReinforcement.csproj -c Release
Copy-Item "WallReinforcement\bin\Release\net8.0-windows\WallReinforcement.*" `
          -Destination "$env:APPDATA\Autodesk\Revit\Addins\2025\"
```

Restart Revit. A new tab **Smart Tools** → panel **Reinforcement** → button **Wall Reinforcement** appears.

The build also copies the `samples/` folder next to the DLL so the in-dialog **New…** picker can find them.

## Using the dialog

1. **Browse…** picks the folder where your `*.json` configs live (path persisted on the .rvt via ExtensibleStorage).
2. **Configuration** dropdown lists every `*.json` in that folder.
3. **New…** copies one of the bundled templates (metric or ACI) into your folder.
4. **Edit raw** opens the selected file in your default JSON editor (so you can comment, reformat, or use editor tooling).
5. The tabs **Cover / Face Mesh / Openings / Edges / Ties / Corners / T-Junctions** let you edit every parameter in place. **Save** writes back to the same file; **Save As…** to a new one.
6. **Units** dropdown switches the interpretation of plain numbers: `Metric` = millimetres, `Imperial` = inches.
7. **Dry run** previews counts in the summary dialog without committing rebar.
8. **Run** does the work inside one `TransactionGroup` per execution.

## JSON schema

```jsonc
{
  "schemaVersion": 1,
  "name": "my-wall-config",
  "units": "Metric",            // or "Imperial"
  "cover": {
    "exterior": 30, "interior": 25,
    "top": 30, "bottom": 30, "ends": 30
  },
  ...
}
```

Lengths are plain numbers (interpreted per `units`) **or** strings in feet-inches syntax that work regardless of `units`:

```jsonc
"length":    24,           // 24 mm or 24 in depending on units
"lapLength": "2'-0\"",     // always 24 inches; ' and " are required
"length":    "1'-3 1/2\""  // mixed feet and fractional inches
```

`barType` values are looked up by `Name` against `RebarBarType` in the active document — the matching family must already be loaded.

### Bundled samples ([`samples/`](samples/))

| File | When to use it |
|---|---|
| [`samples/metric-minimal-200mm.json`](samples/metric-minimal-200mm.json) | SI face-mesh only, Ø10/Ø12 bars at 200 mm |
| [`samples/metric-full-300mm.json`](samples/metric-full-300mm.json)       | SI everything-on, 300 mm wall, monolithic defaults |
| [`samples/aci-minimal-12in.json`](samples/aci-minimal-12in.json)         | ACI 318 face-mesh only, #4 at 12" o.c. |
| [`samples/aci-full-12in.json`](samples/aci-full-12in.json)               | ACI 318 everything-on, 12" wall, #3/#4/#5 bars, ft-in lap lengths |

## Smoke-test recipe

1. Open a project that has the rebar families used in your chosen sample loaded (e.g. `#3 / #4 / #5` ASTM bars for ACI, `Ø10 / Ø12 / Ø14` for SI). If not, edit the `barType` fields in the dialog to match the names you do have.
2. Draw a straight structural wall, ~3 m / 10 ft long, ≥ 250 mm / 10" thick (so ties fire). Place a door or window if you want to see opening trim.
3. Click **Wall Reinforcement** → pick the wall → **Browse…** to your config folder → **New…** to copy `aci-full-12in.json` (or `metric-full-300mm.json`) → edit if needed → **Save** → **Run**.
4. Expected output: 2 `AreaReinforcement` elements (one per face), opening trim + diagonals, U-bars along three edges, transverse ties on a grid.
5. Re-run with the same config → previously-placed bars (tagged `WR:{configName}:{wallId}` in the `Comments` parameter) get deleted before new ones land. Idempotent.
6. Toggle **Dry run** → counts shown in summary, nothing committed.

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

- Lengths come out of the config via `cfg.Ft(length)` which respects `cfg.Units`.
- Shared helpers (`LookupBarType`, `EvenlySpaced`, `Create`) live in [`Engine/RebarFactory.cs`](Engine/RebarFactory.cs).
- Wire your new builder into [`Engine/WallReinforcer.cs`](Engine/WallReinforcer.cs) and tag every placed rebar with the supplied `tag` so re-runs clean up correctly.
