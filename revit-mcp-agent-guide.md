# Revit MCP — Agent Guide

*Orientation for an AI agent (or developer) working on the Revit MCP system: architecture, repo layout, conventions, and how to add a new command end-to-end. Last verified 2026-06-13.*

Companion docs: [`revit-mcp-best-practices.md`](revit-mcp-best-practices.md) (START HERE) · [`revit-mcp-runbook.md`](revit-mcp-runbook.md) (start a session, rebuild, gotchas) · [`revit-mcp-api-cookbook.md`](revit-mcp-api-cookbook.md) (verified C# recipes + pitfalls).

---

## What this is

A bridge that lets an MCP client (Claude) read and drive **Autodesk Revit 2025**. It is a fork of the open-source `mcp-servers-for-revit` project, customized locally (port, extra commands).

- **GitHub:** `dosmanoff/mcp-servers-for-revit` (origin) ← upstream `mcp-servers-for-revit/mcp-servers-for-revit`.
- **Local clone:** `C:\dev\mcp-servers-for-revit` — **local disk only**, never on `L:` (Google Drive breaks `node_modules`).
- **Supported by upstream:** Revit 2020–2026. **We target 2025** (`R25`, .NET 8).

---

## Architecture — four layers

```
MCP Client (Claude)  ⇄ stdio ⇄  MCP Server (TypeScript)  ⇄ WebSocket(:8090) ⇄  Revit Plugin (C#)  →loads→  Command Set (C#)  →executes→  Revit API
```

| Layer | Folder | Language | Role |
| --- | --- | --- | --- |
| MCP Server | `server/` | TypeScript | Declares tools to the AI; forwards calls over WebSocket. Built to `server/build/`. npm pkg `mcp-server-for-revit`. |
| Revit Plugin | `plugin/` | C# | Runs inside Revit; the TCP/WebSocket listener (the **Switch** button); dispatches to commands by reflection. |
| Command Set | `commandset/` | C# | Implements the actual Revit API work, one class per command. |
| Tests | `tests/` | C# | TUnit integration tests against a live Revit. |

Manifest `command.json` (repo root) lists the command set's available commands.

### Local divergences from upstream (preserve across merges)
- **Socket port 8090** (upstream 8080). Set in 4 places: `plugin/Core/SocketService.cs` (×2: field default + hard-wired in `Start`), `plugin/Configuration/ServiceSettings.cs`, `server/src/utils/ConnectionManager.ts`. Reason: 8080 was held by MiniTool ShadowMaker's `MTAgentService`; Revit's `SocketService.Start()` swallows the bind error, so the Switch button silently fails to open.
- **Registered as `revit-mcp-dev`** pointing at the **local build** (`node …\server\build\index.js`), not `npx`, so server edits take effect on rebuild.
- Extra command(s) added locally, e.g. `export_column_rebar`.

---

## Repo layout

```
C:\dev\mcp-servers-for-revit\
├── mcp-servers-for-revit.sln    # plugin + commandset + tests
├── command.json                 # command set manifest (root)
├── server/                      # TypeScript MCP server
│   ├── src/tools/*.ts           # one file per tool; auto-discovered by register.ts
│   └── build/index.js           # compiled entry (what Claude runs)
├── plugin/                      # Revit add-in (C#)
│   ├── Core/SocketService.cs    # listener (port 8090)
│   └── Configuration/ServiceSettings.cs
├── commandset/                  # command implementations (C#)
└── tests/commandset/            # TUnit tests
```

---

## Conventions

- **C# style:** match the surrounding command classes; one command = one `CommandName`.
- **Revit-domain conventions** (from project work, see structural-plugins repo): US norms, **ACI 318**, **Imperial units**, non-seismic default, strict `RebarBarType` lookup. Lengths/diameters from the API come back in **decimal feet** (e.g. #8 = 0.0833 ft = 1 in).
- **`send_code_to_revit`** is the escape hatch for anything without a dedicated tool — see the runbook for its contract (the `document` variable, fully-qualified type names, `auto` vs `none` transaction mode, `PixelSize ≤ 2000`).

---

## Adding a new command (end-to-end)

A command spans **three code layers + the manifest + the TS tool**.

### 1. Command Set (C#) — `commandset/`
Create three pieces:
- **Command** class: extends `ExternalEventCommandBase`, sets `CommandName` (this string is what the plugin matches by reflection).
- **Event handler**: implements `IExternalEventHandler, IWaitableExternalEventHandler`; does the Revit API work inside `Execute(...)`; sets `_resetEvent` in a `finally`.
- **DTO model**: the parameters/result shape.

### 2. Manifest — `command.json` (repo root)
Add an entry for the new command so the command set advertises it.

### 3. MCP Server (TypeScript) — `server/src/tools/<name>.ts`
Export a `register*` function (auto-discovered by `register.ts`) that defines the tool's input schema and calls:
```ts
withRevitConnection(async (conn) => conn.sendCommand("<CommandName>", params))
```

### 4. Build & deploy (the "reload dance")
1. **Close Revit** (build deploys to `%AppData%`; fails on file lock if Revit is open).
2. Build the **command set** (and plugin if changed) with config **`Debug R25`** → deploys DLL + `command.json` to the addins folder.
3. Build the **server**: `cd server && npm run build`.
4. **Open Revit.**
5. Ribbon → **Settings** → **tick the new command** → **Save**. *(The runtime `commandRegistry.json` only holds ENABLED commands — new ones are absent until saved.)*
6. **Switch ON.**
7. **Restart the Claude client** so the Node server respawns and exposes the new tool.

First example added locally: `export_column_rebar` (branch `feat/export-column-rebar`).

---

## Building & testing

**Server:**
```powershell
cd C:\dev\mcp-servers-for-revit\server
npm install
npm run build
```

**Plugin + command set:** open `mcp-servers-for-revit.sln` in Visual Studio, build config **`Release R25`** / **`Debug R25`** (.NET 8 for Revit 2025; `R26` for 2026; `R20`–`R24` are .NET Framework 4.8). Building assembles `plugin/bin/AddIn <year> <config>/` with the command set copied into `Commands/`.

**Tests** (need a live Revit + .NET 10 SDK):
```bash
dotnet test -c Debug.R25 -r win-x64 tests/commandset
```
(Note the commandset maps solution config `Debug R25` → its own `Debug.R25` internally — space vs dot.)

---

## Keeping in sync with upstream
- `git fetch upstream && git merge upstream/main` (or rebase) on a feature branch.
- **Re-apply / verify the 8090 port** in the 4 files after any merge.
- Self-merge own PRs in the fork when GitHub checks are green.

---

## Known open item
- **Rebar tag leader auto-placement is unsolved.** Tried bbox-center / centerline-point / centerline-inside-column / `LeaderEndCondition.Attached` — arrows still miss the visible bar in some cases. Current practice: place leaders by hand. Tagging itself (subelement ref) and section dimensions work.
