# RebarShape pinning — Imperial library 19 vs project's 28_Z_Frame

**Finding from PR #75-#85 investigation; documenting before we forget.**

## Symptom

User sets `LongCrankedShape=19` in `column-assignments.csv`. The plugin
calls `Rebar.CreateFromCurvesAndShape(doc, shape_19, ...)` with our
vertical-diagonal-vertical (V-D-V) curves for a column splice cranked
bar. The bar lands in shape `19` (the pin sticks) but its geometry
is "diagonals at the ends, vertical in the middle" — wrong for a
column splice.

## Root cause

Revit's standard Imperial library `19.rfa` (loaded as RebarShape named
`19` in the project) is a 3-segment shape with topology **D-V-D**
(diagonal-vertical-diagonal), NOT the V-D-V cranked-splice shape we
need. So when Revit re-fits our curves to shape 19's parametric
definition, the long vertical leg lands on the shape's **diagonal**
slot (B/D) and the diagonal lands on the shape's **vertical** slot (C).
The render is exactly what the shape family is — not a bug, just the
wrong family.

Verified via `RebarShapeDefinitionBySegments.GetSegment(i).GetConstraints()`:

| Shape | Seg 0 | Seg 1 | Seg 2 |
|---|---|---|---|
| `19` (Imperial std lib) | D (param B + H/K projected) | **V** (param C + FixedSegmentDir) | D (param D + K/H projected) |
| `28_Z_Frame` (project's custom) | **V** (param C + FixedSegmentDir) | D (param B + D/J projected) | **V** (param A + FixedSegmentDir) |

`28_Z_Frame` has the V-D-V topology we need. Empirically confirmed:
when we pin `28_Z_Frame` and pass our V-D-V curves, segment lengths
are preserved (within bend-allowance tolerance):

```
Input: bottom-vert=10.00ft, diagonal=0.707ft, top-vert=2.250ft
Actual: C=10.015ft        , B=0.741ft       , A=2.265ft
```

## The schedule binding problem

The project's existing schedules (`COLUMN SCHEDULE`,
`Rebar Schedule ST1`, ~60 others) have fields `A`/`B`/`C`/`D` bound to
**shape 19's shared parameter GUIDs**:

```
Schedule 'COLUMN SCHEDULE':
  field 'A' → SharedParameterElement id=6425 GUID=a2eef903-c2bd-4d7f-9075-450374459eb3 → shape 19 'A'
  field 'B' → SharedParameterElement id=6426 GUID=79a7070d-4c41-40a6-b827-d4fa4dfa41e0 → shape 19 'B'
  field 'C' → SharedParameterElement id=6427 GUID=bbb25af7-6bd4-4e63-8930-16355cca08c3 → shape 19 'C'
  field 'D' → SharedParameterElement id=6428 GUID=a05dc65b-9763-4199-b0fe-d773bd5dcc92 → shape 19 'D'
```

`28_Z_Frame`'s `A`/`B`/`C`/`D` use **different shared parameter GUIDs**:

```
'A' GUID=b5ef18b4-453e-49bd-b26c-dfb3bd3ca79c
'B' GUID=bef64550-0992-4b59-a616-1acaa2e24065
'C' GUID=4d1d1719-6bd9-4357-9378-a1d77871e0fd
'D' GUID=93ddaf87-08af-4bb9-b48f-87994feec729
'J' GUID=750b510b-4034-403d-afa7-436272cffa36
```

Same NAMES, different GUIDs. A schedule field can be bound to only one
shared-parameter GUID — so a `28_Z_Frame` bar's values populate the
`28_Z_Frame`-GUID parameters, NOT the schedule field. Net result: the
schedule's `A`/`B`/`C`/`D` columns are EMPTY for `28_Z_Frame` bars
even though both shapes have parameters spelled identically.

This is "merge duplicate parameter fields" — the user asked if we
can fix it on the schedule / runtime side. Answer: **no, GUIDs are
the identity. The fix is at the family-definition level.**

## The trade-off (no-edit, no-tooling case)

|                              | shape `19` (Imperial std)   | shape `28_Z_Frame` (project custom) |
|------------------------------|------------------------------|--------------------------------------|
| Topology                     | D-V-D — **wrong** for splice | V-D-V — **right** for splice         |
| Schedule binding             | matches project schedules    | empty in project schedules           |
| Pinned via `CreateFromCurvesAndShape` | renders shape's defaults (broken geometry) | preserves our V-D-V curves           |

There's no setting in either the plugin or the project that gives BOTH
correct geometry AND correct schedule binding without editing one of:
- The shape family (rebind 28_Z_Frame's A/B/C/D to shape 19's GUIDs)
- Every schedule (rebind A/B/C/D field to 28_Z_Frame's GUIDs)

## Recommended fix

**Edit `28_Z_Frame.rfa` once in Family Editor:**

1. Open the family from project (right-click in Project Browser →
   `Edit`).
2. Family Types → for each of A, B, C, D, J: delete the existing
   shared parameter and re-add via "Manage shared parameters →
   Browse → pick the shared params file that contains GUIDs
   `a2eef903…`/`79a7070d…`/`bbb25af7…`/`a05dc65b…`/(J's GUID)".
3. Each segment constraint that was bound to the old A is
   automatically rebound to the new A by name. Verify visually.
4. Load family back into the project, overwriting existing values.

### API-driven family edit — working approach

The API rebind works via a **two-step swap through a non-shared
intermediate**. `FamilyManager.ReplaceParameter(currentFp, externalDef,
…)` refuses (`Parameter replacement failed.`) when both sides are
shared — Revit silently rejects swapping the GUID under a segment
constraint. But `ReplaceParameter(currentFp, name, group, isInstance)`
(converts shared → non-shared family param, keeping the
`FamilyParameter` id) succeeds, and then `ReplaceParameter(tmpFp,
externalDef, …)` (non-shared → shared with the new GUID) succeeds too.
Both calls preserve the `FamilyParameter` id, so the segment-length
constraint that was bound to it just follows along — no orphaned
constraints, no `not fully defined` error on `LoadFamily`.

The full pseudo-code per parameter (`A`, `B`, `C`, `D`, `J`):

```
shared A (GUID=b5ef18b4-… old)  →ReplaceParameter→  family-param "A_tmp"
family-param "A_tmp"            →ReplaceParameter→  shared A (GUID=a2eef903-… shape 19)
```

(Each step needs its own transaction inside the family doc.)

Verified outcome: bar created in the rebuilt family has **both** sets
of `A`/`B`/`C`/`D`/`J` parameters (the shape 19 ones inherited from the
new shared definitions, the old `28_Z_Frame` ones inherited from the
underlying `Rebar` element's category). The shape 19 ones are the
**active** ones — driven by the segment-length constraints, holding the
correct values from our curves (`A = 27.04"`, `C = 94.54"`, etc.). The
old `28_Z_Frame` GUIDs hold family defaults (`A = 11.81"` etc.) but no
schedule references them, so they're inert.

Reproducible artifacts in `../samples/`:

- `rebind-shape19-guids.csx` — the standalone script (run via revit-mcp
  `send_code_to_revit`, `transactionMode: none`).
- `shape19-params.txt` — the temp shared parameters file with shape
  19's GUIDs and `DATATYPE = REINFORCEMENT_LENGTH`. Required input for
  the script.
- `28_Z_Frame_v19.rfa` — the pre-built output for the 21STR project.
  Load directly with Family → Insert from Library; verify the
  resulting RebarShape's parameters via `LookupParameter("A").GUID`
  reads `a2eef903-c2bd-4d7f-9075-450374459eb3`.
- `21STR-rev4-v19.csv` — assignments CSV variant pointing
  `LongCrankedShape=28_Z_Frame_v19`. Same as `21STR-rev4.csv` for the
  rest. Use this once the v19 family is loaded.

(The earlier `21STR-rev4-28zframe.csv` was the V-D-V variant with the
unmerged GUID caveat; renamed/replaced by `-v19.csv`.)

The manual Family Editor instructions above are still valid as a
fallback if the API path errors on a project with different shape-19
shared params (e.g. a non-Imperial project where `J` GUID differs).

After this one-time edit:
- `28_Z_Frame` bars created by the plugin have parameter values bound
  to the SharedParameterElement ids that all 60+ project schedules
  already reference. Schedules fill in automatically.
- Plugin can continue using `LongCrankedShape=28_Z_Frame` (or empty
  for auto-match — `28_Z_Frame` is the only V-D-V splice in the
  project, so it wins auto-match for our curves).

## Plugin-side guidance (no family edit)

If the family edit can't happen (Sub'l, time pressure, etc.):

- Set `LongCrankedShape=28_Z_Frame` in the CSV. Geometry will be
  right. Schedules will show empty `A`/`B`/`C`/`D` for these bars,
  but `Shape`/`Bar Length`/`Type` still work — manual fill-in or a
  separate Z-bar schedule with `28_Z_Frame`'s GUIDs as a workaround.
- DO NOT set `LongCrankedShape=19` — Revit renders shape 19's own
  default geometry, ignoring our curves.

## Evidence files (out-of-repo, in user's Documents)

- `shape19-def.txt` — shape 19 segment constraints
- `shape-3seg-topologies.txt` — every 3-seg shape in the project, classified
- `28zframe-def.txt` — 28_Z_Frame segment constraints + sample-bar params
- `28zframe-paramnames.txt` — 28_Z_Frame shared param GUIDs + schedule field list
- `shape19-paramguids.txt` — shape 19 shared param GUIDs
- `shape19-vs-28_paramguids.txt` — side-by-side comparison
- `schedule-field-bindings.txt` — every Rebar schedule's A/B/C/D GUID binding
- `test-28zframe-pin.txt` — proof that `CreateFromCurvesAndShape(28_Z_Frame, V-D-V curves)` preserves geometry

(Investigation done via Revit MCP `send_code_to_revit`; preserved on
disk in `C:\Users\Vic\Documents\Claude\Projects\Revit\` if reference
is needed.)
