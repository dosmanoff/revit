# ColumnReinforcement — sample CSV configs

Reference CSV configs for the "From CSV" run mode. Useful for plugin
testing, regression checks, and as templates for new projects.

## Files

| File | Project | Purpose |
|---|---|---|
| `21STR-rev4.csv` | 435E 21 STR (Brooklyn) | User's working CSV. `LongCrankedShape=19` everywhere — the Imperial-std shape 19, which has D-V-D topology and gives WRONG geometry when pinned (see `../docs/shape-pin-guid-finding.md`). Kept verbatim as reference. |
| `21STR-rev4-v19.csv` | 435E 21 STR (Brooklyn) | Same as above with `LongCrankedShape=19` → `=28_Z_Frame_v19`. Use AFTER loading the rebuilt `28_Z_Frame_v19.rfa` (next row) into the project. Geometry is correct (V-D-V), schedule fields `A/B/C/D/J` populate via shape 19's shared-param GUIDs. |
| `28_Z_Frame_v19.rfa` | 435E 21 STR (Brooklyn) | Pre-built rebuild of `28_Z_Frame` with `A/B/C/D/J` rebound to shape 19's shared-parameter GUIDs (`a2eef903-…` etc.). Load via Family → Insert from Library, or `Document.LoadFamily(path, out family)`. The plugin auto-matches the V-D-V topology when `LongCrankedShape=28_Z_Frame_v19` is set in the CSV. |
| `rebind-shape19-guids.csx` | reusable | One-shot Revit MCP script that produces the above `.rfa` from the project's existing `28_Z_Frame`. Run via `send_code_to_revit` with `transactionMode: none`. Useful when porting to a new project (different shape-family element ids — edit the `SHAPE_28_FAMILY_ID` constant at the top). |
| `shape19-params.txt` | reusable | Temp shared-parameters file the script writes into `Application.SharedParametersFilename` to obtain `ExternalDefinition`s for shape 19's `A/B/C/D/J` GUIDs. Required input for `rebind-shape19-guids.csx`. |

## Usage

In the ColumnReinforcement dialog → "From CSV" mode → browse to one of
these files. Select the target columns in Revit first (the plugin
matches by `Mark`).

For development iteration, plumbing for these is the same as the
`From CSV` UI path — load via `AssignmentCsv.Parse(path)` for unit
test fixtures.
