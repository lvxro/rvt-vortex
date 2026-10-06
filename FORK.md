# RVT Vortex — what this fork changes

**RVT Vortex** is a fork of [LuDattilo/revitcortex](https://github.com/LuDattilo/revitcortex), the MCP server that connects Claude (and other models) to Autodesk Revit. All the original work belongs to its author. This fork adds:

1. **Autopilot** — give the AI a long task, walk away, and nothing stalls it.
2. **Revit 2027 install fix** — with the original installers the plugin does not load in Revit 2027.
3. **Interface refresh and localization** — a consistent ribbon with on/off states, and UI text that follows the Windows display language (English, Spanish, Italian).
4. **AI guidance refresh** — fixes guidance that had drifted from the code and makes the usage rules reach every MCP client.

License: MIT, same as the original.

**Naming.** Everything the user sees says **RVT Vortex** (ribbon panel, dialogs, floating window, installer), and the server toggle is now **Vortex Switch**. Internal identifiers are unchanged on purpose — namespaces, the `%USERPROFILE%\.revitcortex` folder, the add-in ID, the `revitcortex` MCP server entry and all tool names — so existing installs and client configurations keep working. Text about services that still belong to the original project (license activation, error telemetry) keeps the RevitCortex name.

**Updates and bug reports point to this fork.** The original update checker read upstream's manifest and offered to install the upstream build with one click, which would have replaced RVT Vortex. It now reads this repository's `latest.json`. "Report a bug" (formerly "Send log to support", which e-mailed the original author) now builds the same diagnostic ZIP and opens a new issue here. Issues are public, so the pre-filled text carries only versions. Both URLs live in one place: `src/RevitCortex.Plugin/ForkInfo.cs`. The plugin version starts at **1.1.0**, above upstream's 1.0.x.

---

## 1. Autopilot

### The problem

RevitCortex asks for confirmation before every operation that edits the model. That's right when the user is present, but if you leave, the first confirmation leaves everything waiting for a click. Revit's own pop-ups (warnings, notices) do the same: they freeze Revit and every tool call queued behind it.

The existing "Auto" mode helped only partly: it could only be turned on from the first confirmation dialog (so you had to wait for one), it didn't cover critical operations (C# scripts), and it didn't touch Revit's pop-ups.

### What it adds

A new ribbon toggle, **Autopilot**. One confirmation to turn it on; while it's on:

| Situation | Behavior |
|---|---|
| Confirmation for an ordinary operation (delete, rename, edit parameters…) | Approved automatically |
| C# script (`send_code_to_revit`) | **Declined** without opening a dialog, unless the user ticks "Also allow C# scripts" when turning Autopilot on |
| Revit pop-up | Closed automatically, preferring Close / Cancel / No; OK only when there is no other choice |
| Document switch | Stops auto-approving: anything needing confirmation is declined, so a model the user didn't pick is never edited |
| Document closed | Autopilot turns off |
| Model changes | **Auto-save** when Revit is idle, at most once a minute; each save leaves Revit's backup copy (`.0001.rvt`, `.0002.rvt`…) |

When an operation is declined under Autopilot, the plugin rewrites the hint the AI receives. The original said "ask the user if they want to retry"; under Autopilot it says "the user is away: skip the step, keep going and list it as pending". Otherwise the AI would stop and wait for an answer that never comes.

Everything decided without the user goes to `%USERPROFILE%\.revitcortex\autopilot.log`: operations approved or declined, pop-ups closed, and saves.

User guide, including a ready-to-use prompt: [docs/AUTOPILOT.md](docs/AUTOPILOT.md).

### Design choices

- **Opt-in, and nothing changes if you don't use it.** Without clicking Autopilot, the plugin behaves like the original.
- **Fail closed.** Anything that would need a human decision is declined, not approved. The worst outcome is a pending step, not an unwanted change.
- **C# scripts still go through the plugin's code sandbox** (no file, network, registry or process access), even when allowed without confirmation.
- **RevitCortex's own dialogs are never auto-dismissed.** The pop-up handler tells them apart from Revit's.

### Files

| File | Role |
|---|---|
| `src/RevitCortex.Core/Session/CortexSession.cs` | `UnattendedMode`, `UnattendedAllowCritical`, `AutoDecision` event, approve/decline logic |
| `src/RevitCortex.Core/Session/AutoSaveScheduler.cs` | Decides when to save (pending changes + one-per-minute throttle) |
| `src/RevitCortex.Plugin/Commands/ToggleAutopilot.cs` | Ribbon toggle and start dialog |
| `src/RevitCortex.Plugin/UI/UnattendedDialogHandler.cs` | Closes Revit pop-ups (`DialogBoxShowing`) |
| `src/RevitCortex.Plugin/UI/UnattendedLog.cs` | Writes `autopilot.log` |
| `src/RevitCortex.Plugin/UI/ConfirmationHelper.cs` | Marks RevitCortex's own dialogs so they are never auto-closed |
| `src/RevitCortex.Plugin/CortexRouter.cs` | Marks pending saves and rewrites the "cancelled" hint |
| `src/RevitCortex.Plugin/RevitCortexApp.cs` | Wiring, start/stop, saving on `Idling` |
| `src/RevitCortex.Tests/...` | Tests for session, auto-save and router behavior |
| `CLAUDE.md` | Rules so the AI knows how to behave under Autopilot |

---

## 2. Revit 2027 install fix

### The problem

Revit 2027 no longer loads "all users" add-in manifests from `C:\ProgramData\Autodesk\Revit\Addins\2027`. The Revit journal says:

```
Add-in manifest file from: C:\ProgramData\Autodesk\Revit\Addins\2027\RevitCortex.addin,
won't be loaded. All-users Add-in manifest files must be installed to:
C:\Program Files\Autodesk\Revit\Addins\2027
```

Both `deploy.ps1` and the distribution installer (`distribution/lib/RevitDeploy.ps1`) installed to `ProgramData` and then **deleted the per-user copy** to avoid duplicates. On Revit 2027 that leaves no valid copy at all: the RevitCortex ribbon disappears and the MCP server has nothing to talk to.

### The fix

For Revit 2027 and later, both scripts install to the per-user folder (`%APPDATA%\Autodesk\Revit\Addins\2027`), which Revit still loads, and remove the ignored `ProgramData` copy. Revit 2023–2026 are unchanged.

---

## 3. Interface and localization

- **Ribbon:** the two toggles (Cortex Switch, Autopilot) are large buttons, **grey when off and Claude orange (#D97757) when on**. Autopilot's label also changes to *Autopilot ON*. Settings, License, Power BI and Support are grouped as small stacked buttons in a single slate color instead of five unrelated colors.
- **Floating window:** restyled with the orange accent, a pulsing status dot, and the latest automatic decision or save.
- **Localization:** UI language now follows the **Windows display language** first (Revit's language and the thread culture are fallbacks). Spanish was added to every existing localized string. The new ribbon labels, confirmation dialogs, Autopilot dialog, floating window and log come in English, Spanish and Italian.

Not localized yet: the Settings window and the Power BI export window keep their original text.

---

## 4. AI guidance refresh

The project is about nine months old and its guidance for the AI had drifted from the code. Fixed:

- **Rules now reach every client.** The detailed usage guide lived in `CLAUDE.md`, which only coding agents working inside the repo read; Claude Desktop never sees it. The essentials (language detection, tool choice, token limits, dry runs, cancellations, Autopilot, scripts) are now in the MCP `ServerInstructions` that every client receives on connect.
- **`ai_element_filter` / `operate_element`:** the guides said a `data` wrapper was *required*, but the MCP server already adds it. Following the guide produced failing calls. They now say to pass parameters flat.
- **Parameter filters:** the guides sent range/AND-OR parameter filters to `ai_element_filter`, which does not filter on parameter values; they now point to `filter_by_parameter_value` with its `conditions` array.
- **Stale defaults:** `get_warnings` defaults to 50 (not 500); `get_project_info` already leaves worksets and links off.
- **Language detection:** `say_hello` returns the detected locale at almost no cost; its description wrongly said it showed a greeting in Revit.
- **Tool descriptions:** the most used and most expensive tools now carry their usage hints (`compact`, `summaryOnly`, limits, cheapest-first health checks) in their own descriptions.

Fixed in `CLAUDE.md`, `AGENTS.md`, `WORKFLOWS.md` and the `ai-skills` tool-selection reference. A note at the top of `CLAUDE.md`/`AGENTS.md` points to the files every client reads, to prevent the same drift.

---

## Releasing a new version

Publishing is automated by `.github/workflows/release.yml`:

```bash
git tag v1.1.1
git push origin v1.1.1
```

GitHub then builds every Revit version with `build-release.ps1`, attaches `RVT-Vortex-v1.1.1.zip` to a new GitHub Release (with auto-generated notes), and rewrites `latest.json` on `main` with the version, download URL and SHA-256. Installed copies pick it up the next time Revit starts and offer the update.

Use a version higher than the one installed (the plugin compares versions). Watch the run under the repository's **Actions** tab; if a Revit version fails to build, `build-release.ps1` skips it and the others still ship.

---

## Installation

With Revit closed, run **`INSTALL.bat`**. It will:

1. Detect which Revit versions have RevitCortex installed.
2. Install the .NET SDK if missing.
3. Back up the current installation to the Desktop.
4. Build and install with `deploy.ps1`.

The installer's messages follow the Windows language (English or Spanish). To build by hand, see "Building from Source" in the [README](README.md).

## Status

- The Revit 2027 fix comes from a real case: the Revit 2027 journal showed the message above and the plugin didn't load.
- Autopilot's session, auto-save and router logic is covered by unit tests. The Revit-dependent parts (ribbon toggle, pop-up handling, saving on `Idling`) and the new interface need testing inside Revit.
- Revit 2023–2026 have not been tested with this fork.

If something breaks, the backup made by the installer includes instructions to roll back. Please report problems in this repository's issues.
