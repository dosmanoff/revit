# Revit MCP — Runbook

*Operational guide: how to start a session, test the link, rebuild after changes, and recover from common problems. Last verified 2026-06-13.*

Companion doc: [`revit-mcp-agent-guide.md`](revit-mcp-agent-guide.md) (architecture + how to add a command).

---

## TL;DR — start a working session

1. **Open Revit 2025** and load the `.rvt` you want to work on.
2. Revit ribbon → **"Revit MCP Plugin"** panel → click **Switch** so the TCP listener turns **ON** (port **8090**). The button label flips from "Open Server" to running.
3. *(Only if you need a command that isn't enabled yet)* → **Settings** → tick the command(s) → **Save**.
4. Start the **Claude client** (Claude Code in any folder, or Claude Desktop). The MCP server auto-spawns — no manual launch.
5. Confirm the link with a **non-interactive** call: `get_current_view_info`. (Don't use `say_hello` as a health check — it pops a modal that must be clicked within 15 s.)

If `get_current_view_info` returns the active view, you're live.

---

## Current local setup (as registered)

- **MCP server registration** (Claude Code, **user scope** — every session sees it):
  ```
  revit-mcp-dev:  C:\Program Files\nodejs\node.exe  C:\dev\mcp-servers-for-revit\server\build\index.js
  ```
  Check anytime with `claude mcp list` (look for `revit-mcp-dev … ✓ Connected`).
- **Repo / clone:** `C:\dev\mcp-servers-for-revit` (fork `dosmanoff/mcp-servers-for-revit`; upstream `mcp-servers-for-revit/mcp-servers-for-revit`). **Not on `L:` / Google Drive** — `npm`/`node_modules` must live on local disk.
- **Socket port: 8090** (local divergence from upstream's 8080 — keep across merges; see Gotchas).
- **Revit version:** 2025 → build config **`Release R25` / `Debug R25`** (.NET 8).

> The registration uses the **locally built** `server/build/index.js` (dev workflow), not the published `npx mcp-server-for-revit`. That's intentional so local server edits take effect after a rebuild.

---

## Connection test snippets

Non-interactive ping via `send_code_to_revit`:
```csharp
return "ping " + document.Title;
```
Or the dedicated tool: `get_current_view_info` (returns active view name/id).

---

## Rebuild / reload after editing the code

Which layer you changed determines what you restart.

### A. Edited the **TypeScript server** (`server/src/**`, e.g. a tool's schema)
```powershell
cd C:\dev\mcp-servers-for-revit\server
npm install      # only if deps changed
npm run build    # compiles to server/build/
```
Then **restart the Claude client** so the Node server respawns and re-exposes tools.

### B. Edited the **Revit plugin** (`plugin/**`) or **command set** (`commandset/**`)
1. **Close Revit** (the build deploys DLLs into `%AppData%` and **fails with a file lock if Revit is open**).
2. Build the affected project in Visual Studio with config **`Debug R25`** (or `Release R25`). Building the solution assembles the full deployable layout in `plugin/bin/AddIn <year> <config>/` and copies the command set into the plugin's `Commands/` folder.
3. **Open Revit.**
4. If you **added a new command**: ribbon → **Settings** → tick it → **Save**. *(The runtime `Commands/.../commandRegistry.json` only contains ENABLED commands; a freshly built command is absent until you save.)*
5. **Switch ON.**
6. **Restart the Claude client** if the server side (TS tool) also changed.

Full end-to-end "add a command" walkthrough lives in the agent guide.

---

## Tool inventory (currently exposed)

Read/query: `get_current_view_info`, `get_current_view_elements`, `get_selected_elements`, `get_available_family_types`, `get_material_quantities`, `ai_element_filter`, `analyze_model_statistics`, `export_room_data`, `export_column_rebar`, `query_stored_data`.

Create/modify: `create_point_based_element`, `create_line_based_element`, `create_surface_based_element`, `create_grid`, `create_level`, `create_room`, `create_dimensions`, `create_structural_framing_system`, `delete_element`, `operate_element` (select/hide/setColor/…), `color_elements`, `tag_all_walls`, `tag_all_rooms`.

Data store: `store_project_data`, `store_room_data`.

**Workhorse:** `send_code_to_revit` — runs arbitrary C# inside Revit. This is how all the rebar/MRA/view-export automation is done.

### `send_code_to_revit` contract (gotchas that bite)
- The snippet body runs inside an `Execute(Document, object[])` method — `Document` is available as **`document`**.
- **No `using` alias directives or namespace-scoped statements** inside the body (it's a method body). Use **fully-qualified type names** instead, e.g. `Autodesk.Revit.DB.Structure.Rebar`, `Autodesk.Revit.DB.Structure.RebarPresentationMode` (the bare `Rebar` type is inaccessible / protected in this context).
- `transactionMode`: **`auto`** wraps your code in a transaction (use for any model change, incl. setting view params like `VIEW_SCALE`); **`none`** for pure reads/queries and for `ExportImage` (no txn needed). Setting parameters under `none` throws.
- Return a `string` (often a `StringBuilder`) — it comes back as the result.
- `ElementId` takes a `long`/`int`; build with `new ElementId(123456)`.
- Image export: `ImageExportOptions` with **`PixelSize <= 2000`** so the PNG is readable by the image tools; file path has the view name appended automatically.

---

## Gotchas / troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| **`connect to revit client failed`** or MCP calls hang | Revit isn't running, the **Switch** is OFF, or a modal dialog is blocking Revit. Check `Get-Process Revit`; if absent, reopen Revit + Switch ON. **Don't open a second Revit instance.** |
| **Switch button stays "Open Server"** (won't bind) | Port already in use. We moved 8080 → **8090** because MiniTool ShadowMaker's `MTAgentService` held 8080 and Revit's `SocketService.Start()` swallows the bind error silently. Verify nothing else holds 8090. |
| **New command not visible to Claude** | Forgot **Settings → tick → Save** in Revit, and/or didn't restart the Claude client after the TS build. |
| **Build fails with file lock** | Revit is open. Close it, rebuild, reopen. |
| `say_hello` "fails" | It only reports success if the modal is clicked within 15 s. Use `get_current_view_info` for health checks. |
| Image won't load back for review | `PixelSize` > 2000. Re-export at ≤ 1900. |

---

## Documentation locations

All Revit-MCP docs live in **`L:\My Drive\claude\revit\claude_revit_mcp\`** (`.md` on Drive is fine):
- `revit-mcp-runbook.md` — this file (startup / rebuild / gotchas).
- `revit-mcp-agent-guide.md` — architecture, repo layout, adding a command.

The **code** lives at `C:\dev\mcp-servers-for-revit` (local disk only). This repo (`claude_revit_mcp`) is the *structural-plugins* repo (AutoNumbering, ColumnReinforcement, SlabRebar, SmartViews, WallReinforcement) — separate from the MCP server.
