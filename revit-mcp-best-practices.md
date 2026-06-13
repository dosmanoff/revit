# Revit MCP — Best Practices (START HERE)

*Read this first at the start of any Revit-MCP modeling session. It's the entry point to the doc set and the short list of rules that otherwise get re-learned through errors. Last verified 2026-06-13.*

> **Why this doc exists:** every new session the agent was rediscovering the same rebar/view/annotation gotchas. The fixes are written down — use them, don't re-derive.

---

## The doc set (read order)

1. **`revit-mcp-best-practices.md`** ← you are here. Orientation + top rules.
2. **[`revit-mcp-runbook.md`](revit-mcp-runbook.md)** — start a session, connection test, rebuild-after-change, tool inventory, `send_code_to_revit` contract, "orient yourself first".
3. **[`revit-mcp-api-cookbook.md`](revit-mcp-api-cookbook.md)** — verified copy-paste C# recipes (rebar, MRA, views, export) + full pitfalls table. **Open this before writing any `send_code_to_revit` for rebar/views/annotations.**
4. **[`revit-mcp-agent-guide.md`](revit-mcp-agent-guide.md)** — architecture, repo layout, adding a command.

All four live at the repo root **`L:\My Drive\claude\revit\`** (NOT in the `claude_revit_mcp\` subfolder, which only holds git worktrees). The same rules are mirrored in auto-memory (`revit-modeling-best-practices`) so they load every session.

---

## Orient before you build (read-only first moves)

- `get_current_view_info` — confirm the MCP link + active view/model.
- List levels: `FilteredElementCollector(document).OfClass(typeof(Level))` — the elevation datums.
- `analyze_model_statistics` — model scope.
- **Probe the target's geometry before editing** (bbox, location curve, solid faces — cookbook §2). Don't assume a wall/slab is a plain rectangle; it may be stepped/sloped.
- When replicating across floors, verify against the **reference/etalon view**, not raw coordinates.

---

## Top rules (the ones that get violated most)

**`send_code_to_revit` basics**
1. Use **fully-qualified** `Autodesk.Revit.DB.Structure.Rebar` / `RebarBarType` / `RebarShape` / `RebarPresentationMode` — bare names are inaccessible; **no `using` aliases** in the body.
2. `transactionMode`: **`auto`** for any change (incl. setting view params), **`none`** for reads and `ExportImage`. `.Set()` under `none` throws.
3. Lengths/diameters come back in **decimal feet** (#8 = 0.0833 ft = 1").

**Rebar**
4. **Edit in place** (`ChangeTypeId` + `ScaleToBox` / set segment params) — **never delete+recreate** when tags / MRA / bending details exist; that destroys them, often unrecoverably.
5. **`SetUnobscuredInView` is NOT copied** when you `CopyElements` rebar — re-apply per view (the "invisible bars on other floors" bug). Same for presentation mode / hidden positions.
6. Avoid `ScaleToBox` non-determinism → set segment params directly (`LookupParameter("B").Set(...)`) to stop schedule **"varies"**.
7. **Strict `RebarBarType` lookup** — never silently fall back to a default size; report if the requested size is missing.
8. For bent bars / hooks that "fly away," build the bar from explicit polyline curves (`CreateFromCurves`) so Revit recognises the shape.

**Annotations**
9. Tag bars (incl. cut bars) via the **subelement ref** `uid:200000X:SUBELEMENT`; `new Reference(rebar)` fails. Dimensions use **PlanarFace / silhouette** refs.
10. **Hiding an MRA wrapper does NOT hide its child Dimension/Tag** — hide/move them directly via `mra.DimensionId` / `mra.TagId`.
11. A **Linear MRA needs references perpendicular to the dim line**; a single-bar set has nothing to dimension internally (throws) — dimension a multi-position set or a grid.
12. Leader auto-placement is **unsolved** — arrows can miss the bar; leave final leader placement to the user.

**Views / review**
13. Section: `ViewSection.CreateSection` with a `Transform` + `BoundingBoxXYZ`. 3D: `View3D.CreateIsometric` + `SetSectionBox` + `IsolateElementsTemporary`. Hide the Rebar category to read concrete.
14. Export for review with `ImageExportOptions`, **`PixelSize ≤ 2000`**, then read the PNG back to verify.

---

## Conventions (this practice)

US norms / **ACI 318**, **Imperial units**, **non-seismic** default. Verify against the engineer's reference details before mass-replicating reinforcement.

## Keeping this current

When a new gotcha surfaces in a session, add it to the **cookbook** (source of truth); if it's high-frequency, also add a line here and to the `revit-modeling-best-practices` auto-memory. That's how we stop paying the same tuition twice.
