<p align="center">
  <img src="docs/assets/banner.svg" alt="RVT Vortex — let AI keep working in Autodesk Revit, even when you walk away" width="100%">
</p>

<p align="center">
  <a href="https://github.com/lvxro/rvt-vortex/releases/latest"><img src="https://img.shields.io/github/v/release/lvxro/rvt-vortex?color=D97757&label=release" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/Revit-2023%E2%80%932027-3F4A55" alt="Revit 2023–2027">
  <img src="https://img.shields.io/badge/MCP-288%20tools-3F4A55" alt="288 MCP tools">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-3F4A55" alt="MIT license"></a>
</p>

<p align="center">
  <a href="#quick-start"><b>Quick start</b></a> ·
  <a href="docs/AUTOPILOT.md"><b>Autopilot guide</b></a> ·
  <a href="FORK.md"><b>What changed</b></a> ·
  <a href="docs/REVITCORTEX_README.md"><b>Full tool reference</b></a>
</p>

---

**RVT Vortex** connects Claude — or any AI client that speaks the [Model Context Protocol (MCP)](https://modelcontextprotocol.io) — to a live Autodesk Revit model. The AI can query, create, edit and audit the model through 288 dedicated tools.

It is a fork of [**RevitCortex**](https://github.com/LuDattilo/revitcortex) by Luigi Dattilo, built around one idea: **you should be able to hand the AI a long task and leave.**

## Why a fork

<table>
  <tr>
    <td width="33%" valign="top">
      <h4>⏸️ One dialog stalls everything</h4>
      Every edit asks for confirmation, and Revit's own pop-ups freeze the session. If you leave, the AI waits at the first click it needs.
    </td>
    <td width="33%" valign="top">
      <h4>🧭 The AI never saw the rules</h4>
      Usage guidance lived in a file only coding agents read. Claude Desktop never saw it, and parts of it had drifted from the code.
    </td>
    <td width="33%" valign="top">
      <h4>🚫 Revit 2027 didn't load it</h4>
      Revit 2027 ignores add-ins installed in <code>ProgramData</code>, and the installer then deleted the copy that worked.
    </td>
  </tr>
</table>

## What RVT Vortex adds

| | RevitCortex | RVT Vortex |
|---|---|---|
| **Leave the AI working** | Confirm every edit; "Auto" only from the first dialog | **Autopilot** toggle: no dialog can stall the session |
| **Revit pop-ups** | Wait for a click | Closed automatically, choosing Cancel/Close when possible |
| **Saving** | Manual | Auto-save after changes, at most once a minute, with Revit backups |
| **What happened while you were away** | Technical audit log | Readable `autopilot.log` + live status window |
| **Revit 2027** | Add-in not loaded | Installs to the per-user folder that Revit 2027 loads |
| **AI guidance** | Only in `CLAUDE.md` | Sent to every MCP client on connect, with stale rules fixed |
| **Interface** | English / Italian | Follows the Windows language: English, Spanish or Italian |
| **Updates** | — | One-click updates from this repo's releases |

## How Autopilot works

Click **Autopilot** in the ribbon — it turns from grey to orange — and every point where the session used to stop now has an answer:

```mermaid
flowchart LR
    A([AI calls a tool]) --> B{Needs a<br/>confirmation?}
    B -- "ordinary edit" --> C[Approved]
    B -- "C# script" --> D{Scripts allowed<br/>at start?}
    D -- yes --> C
    D -- no --> E[Declined —<br/>AI skips it and goes on]
    B -- no --> C
    C --> F[Revit pop-up?<br/>Closed: Cancel / Close]
    F --> G[(Model saved<br/>when Revit is idle)]
    E --> H[[autopilot.log]]
    G --> H
```

It **fails closed**: anything that would need a human decision is declined, never approved. The worst case is a step left pending, not an unwanted change. Details and a ready-to-use prompt are in the [Autopilot guide](docs/AUTOPILOT.md).

## Quick start

### 1 · Download

| | |
|---|---|
| **Revit plugin + MCP server** | [`RVT-Vortex-vX.Y.Z.zip`](https://github.com/lvxro/rvt-vortex/releases/latest) — Revit 2023, 2024, 2025, 2026 and 2027 |
| **Requirements** | Windows 10/11 · .NET Desktop Runtime 8 (Revit 2025–2026) or 10 (Revit 2027) |

### 2 · Install

Close Revit, unzip, and run **`install.bat`**. It detects your Revit versions, installs the plugin and the MCP server, and can add the server to Claude Desktop for you.

Then open Revit: the **Add-Ins** tab has an **RVT Vortex** panel.

### 3 · Connect your AI client

<details open>
<summary><b>Claude Desktop</b></summary>

Edit `%APPDATA%\Claude\claude_desktop_config.json` (the installer can do it for you) and restart Claude Desktop:

```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "C:\\Users\\<you>\\.revitcortex\\server\\RevitCortex.Server.exe"
    }
  }
}
```
</details>

<details>
<summary><b>Claude Code</b></summary>

```bash
claude mcp add revitcortex "C:\Users\<you>\.revitcortex\server\RevitCortex.Server.exe"
```
</details>

<details>
<summary><b>Codex, Cursor, Continue, Cline, Gemini CLI…</b></summary>

Any client with stdio MCP support works. Setup for each one is in the [full reference](docs/REVITCORTEX_README.md#mcp-client-setup--choose-your-client).
</details>

> The internal names (`revitcortex` entry, `.revitcortex` folder) are kept on purpose, so existing RevitCortex setups keep working.

### 4 · Try it

In Revit, click **Vortex Switch** (it turns orange), then ask your AI client:

> *Check this model's health and list the five most common warnings.*

## The ribbon

| Button | |
|---|---|
| **Vortex Switch** | Starts / stops the server the AI talks to. Grey = off, orange = on. |
| **Autopilot** | Lets the AI keep working without anyone clicking. Grey = off, orange = on. |
| Settings · License · Power BI Export · Report a bug | Configuration, license, export wizard, and a diagnostic ZIP + new GitHub issue. |

## What the AI can do

| Area | Tools | Examples |
|---|---:|---|
| Rebar | 64 | create, shape, splice, number and annotate reinforcement |
| Structural steel | 48 | connections, cuts, fabrication data |
| Elements & Power BI | 36 | query, filter, select, copy, measure; publish to Power BI |
| Project & workflows | 35 | health check, warnings, purge, clash detection, tags, levels, rooms, sandboxed C# scripts |
| Views & sheets | 24 | views, templates, filters, sheets, viewports, schedules |
| Creation & exchange | 22 | point/line/surface-based elements, floors, grids, dimensions; Excel and CSV import/export |
| IFC | 20 | export, link, rebuild IFC geometry as native elements |
| Links | 13 | load, move, pin and inspect linked models |
| Parameters | 11 | set, bulk edit, CSV sync, shared/global/project parameters |
| Materials | 9 | materials, compound structures, quantities |
| Meta | 6 | connection check, project info, cache, cross-app selection |

Full list with parameters: [`tool-schemas.txt`](tool-schemas.txt) · descriptions: [full reference](docs/REVITCORTEX_README.md#tool-reference).

## Safety

- **Preview first** — tools that change the model default to `dryRun: true`.
- **Confirmations** — destructive edits ask before running, unless you turn on Autopilot.
- **Sandboxed scripts** — `send_code_to_revit` blocks file, network, registry and process access.
- **Audit log** — every call goes to `%USERPROFILE%\.revitcortex\audit.jsonl`.
- **Read-only mode** — one setting blocks every write tool.

## Building from source

Clone the repo, close Revit and run **`INSTALL.bat`**. It checks for the .NET SDK, backs up your current install and builds with `deploy.ps1`. Build commands for each Revit version are in [`CLAUDE.md`](CLAUDE.md#build-commands).

Releases are automated: pushing a `vX.Y.Z` tag (or publishing a release on GitHub) builds every Revit version and attaches the ZIP. See [FORK.md](FORK.md#releasing-a-new-version).

## Credits

RVT Vortex is built on [RevitCortex](https://github.com/LuDattilo/revitcortex) by **Luigi Dattilo** — the server, the 288 tools and the plugin architecture are his work. This fork adds Autopilot, the Revit 2027 fix, the interface refresh and the updated AI guidance.

Released under the [MIT license](LICENSE), like the original.

<p align="center"><sub>Found a bug? Use <b>Report a bug</b> in the ribbon, or <a href="https://github.com/lvxro/rvt-vortex/issues/new">open an issue</a>.</sub></p>
