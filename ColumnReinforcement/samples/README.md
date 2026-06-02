# ColumnReinforcement — sample CSV configs

Reference CSV configs for the "From CSV" run mode. Useful for plugin
testing, regression checks, and as templates for new projects.

## Files

| File | Project | Purpose |
|---|---|---|
| `21STR-rev4.csv` | 435E 21 STR (Brooklyn) | User's working CSV. `LongCrankedShape=19` everywhere — the Imperial-std shape 19, which has D-V-D topology and gives WRONG geometry when pinned (see `../docs/shape-pin-guid-finding.md`). Kept verbatim as reference. |
| `21STR-rev4-28zframe.csv` | 435E 21 STR (Brooklyn) | Same as above with `LongCrankedShape=19` → `=28_Z_Frame`. Geometry is correct (V-D-V topology matches our intent) but schedule fields `A`/`B`/`C`/`D` will be empty unless `28_Z_Frame` is re-bound to shape 19's shared-param GUIDs (see the same doc). |

## Usage

In the ColumnReinforcement dialog → "From CSV" mode → browse to one of
these files. Select the target columns in Revit first (the plugin
matches by `Mark`).

For development iteration, plumbing for these is the same as the
`From CSV` UI path — load via `AssignmentCsv.Parse(path)` for unit
test fixtures.
