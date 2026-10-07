# RVT Vortex — what this fork changes

**RVT Vortex** is a fork of [LuDattilo/revitcortex](https://github.com/LuDattilo/revitcortex), the MCP server that connects Claude (and other models) to Autodesk Revit. All the original work belongs to its author. This fork adds:

1. **Autopilot** — give the AI a long task, walk away, and nothing stalls it.
2. **Revit 2027 install fix** — with the original installers the plugin does not load in Revit 2027.
3. **Interface refresh and localization** — a consistent ribbon with on/off states, and UI text that follows the Windows display language (English, Spanish, Italian).
4. **AI guidance refresh** — fixes guidance that had drifted from the code and makes the usage rules reach every MCP client.
5. **No license gate** — fully open source: no Premium license, trial or activation.

License: MIT, same as the original.

**Naming.** Everything the user sees says **RVT Vortex** (ribbon panel, dialogs, floating window, installer), and the server toggle is now **Vortex Switch**. Internal identifiers are unchanged on purpose — namespaces, the `%USERPROFILE%\.revitcortex` folder, the add-in ID, the `revitcortex` MCP server entry and all tool names — so existing installs and client configurations keep working. Text about services that still belong to the original project (license activation) keeps the RevitCortex name.

**Updates and bug reports point to this fork.** The original update checker read upstream's manifest and offered to install the upstream build with one click, which would have replaced RVT Vortex. It now reads this repository's `latest.json`. "Report a bug" (formerly "Send log to support", which e-mailed the original author) now builds a diagnostic ZIP and opens a new issue here. Issues are public, so the pre-filled text carries only versions and the ZIP is redacted by default: no user or machine name, no model names or paths, no tool inputs or script source, no Revit journal (the audit log is reduced to tool name, outcome, timing and a scrubbed error message). To get the full package for a private exchange, set `"SupportReportIncludePrivateData": true` in `settings.json`; in that mode no public issue is opened. Both URLs live in one place: `src/RevitCortex.Plugin/ForkInfo.cs`. The plugin version starts at **1.1.0**, above upstream's 1.0.x.

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
- **C# scripts still go through the plugin's script filter**, even when allowed without confirmation. The filter rejects code containing common file, network, registry, process or reflection calls. It is a text check that catches accidents and obvious misuse, not an isolation boundary: a script that passes runs inside Revit with the user's Windows permissions. Ticking "Also allow C# scripts" therefore means trusting whatever the AI writes during that session, including anything it was led to write by text it read in a model or a linked file.
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

### Duplicate add-in ID (clash with Autodesk Forma)

Upstream used the placeholder add-in ID `A1B2C3D4-E5F6-7890-ABCD-EF1234567890`. Revit 2027 ships Autodesk's **FormaOpenIn** add-in (`C:\Program Files\Autodesk\Revit 2027\AddIns\FormaOpenIn`) registered under that same ID. With both installed:

- Revit refuses to start FormaOpenIn ("duplicate add-in ID" dialog on every launch).
- Ribbon commands are resolved by add-in ID, land in Forma's load context, and load a second copy of the plugin whose startup never ran, so every button answers "plugin not initialized".

RVT Vortex now has its own ID (`50E95FD2-C886-49CC-A887-FDA65FA86CD1`), which fixes both and also lets it coexist with an original RevitCortex install. Confirmed on Revit 2027: with the new ID both toggles work. The installers also remove a stale RevitCortex manifest from the 2027 all-users folder (`C:\Program Files\Autodesk\Revit\Addins\2027`), or warn when they lack admin rights.

### The fix

For Revit 2027 and later, both scripts install to the per-user folder (`%APPDATA%\Autodesk\Revit\Addins\2027`), which Revit still loads, and remove the ignored `ProgramData` copy. Revit 2023–2026 are unchanged.

---

## 3. Interface and localization

- **One dark theme:** Settings, the update notice, the Autopilot pill and its summary, and the Power BI export share one palette and one set of controls (`UI/Theme.xaml`). Orange means *on* or the main action, a light pill marks the selected option, amber means something needs attention.
- **Ribbon:** the two toggles (Vortex Switch, Autopilot) are large buttons on a **dark tile when off and Claude orange (#D97757) when on**; their labels change too (*Vortex ON*, *Autopilot ON*). The Vortex Switch icon is the RVT Vortex mark. Settings, Power BI and Report a bug are small stacked buttons on the same dark tile.
- **Status pill:** while Auto mode or Autopilot is on, a compact dark pill sits bottom-center. Its mark spins while the AI is calling tools; it shows how many edits were approved and declined, the latest event, whether the model is saved, and an amber warning when nothing can happen (another document is active, or the server is off). An arrow opens an activity panel with totals and the last events. Drag it anywhere.
- **Autopilot summary:** when Autopilot stops, a small window shows how long it ran, the totals and the steps it declined.
- **Settings:** two tabs (General, Tools). Each setting is a row with its explanation on the left. The server can be started and stopped from here, and the tools list has a search box, an enabled/disabled filter and a per-category switch.
- **Power BI export:** two steps. *Data*: pick where the rows come from (whole model, active view, current selection, or the schedules that already exist), tick categories, and move parameters into the list of CSV columns. *Output*: folder and file name, overwrite / export on save, the link with Power BI, a preview of the first rows, and (collapsed) the column types. Profiles are in a menu at the top right, and loading one now restores its columns too. The columns come out in the order of that list, instance parameters first and type parameters after them (before, the order was whatever Revit enumerated). The footer says what is chosen, or in amber what is missing.
- **Localization:** UI language now follows the **Windows display language** first (Revit's language and the thread culture are fallbacks). Spanish was added to every existing localized string. The new ribbon labels, confirmation dialogs, Autopilot dialog, floating window, log and the Power BI export window come in English, Spanish and Italian.

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

## 5. No license gate

Upstream ships a "RevitCortex Premium" license with a trial; without a valid license, every tool that edits the model is blocked, and activation keys come from the original author's service. RVT Vortex is plain open source: the license gate is never initialized (the router treats a missing gate as "allow everything"), the License & Account button is gone from the ribbon, and "Premium" no longer appears in the UI. The licensing code stays in the tree, unused, so upstream's tests keep compiling.

The update notification (previously Italian-only) now follows the Windows language.

**No telemetry.** Upstream can send opt-in error reports to the original project's ingest service. That service is not part of this fork, so RVT Vortex keeps the code but never starts it (`ForkInfo.TelemetryEnabled` is `false`): nothing is queued or sent, Revit no longer asks for telemetry consent on first run, and the toggle is hidden in Settings.

---

## Releasing a new version

Publishing is automated by `.github/workflows/release.yml`:

```bash
git tag v1.1.1
git push origin v1.1.1
```

Publishing from the GitHub web page works too (Releases → Draft a new release → new tag `v1.1.1` → Publish): the workflow attaches the ZIP to that release instead of creating a second one.

GitHub then builds every Revit version with `build-release.ps1`, attaches `RVT-Vortex-v1.1.1.zip` to a new GitHub Release (with auto-generated notes), and rewrites `latest.json` on `main` with the version, download URL and SHA-256. Installed copies pick it up the next time Revit starts and offer the update.

Use a version higher than the one installed (the plugin compares versions). Watch the run under the repository's **Actions** tab; the run first executes the unit tests, and if they fail or any Revit version fails to build, nothing is published (`build-release.ps1 -AllowSkip` still builds a partial package locally).

Every push to `main` and every pull request also runs `.github/workflows/ci.yml`: the unit tests plus a full package build for all five Revit versions.

---

## Installation

With Revit closed, run **`INSTALL.bat`**. It will:

1. Detect which Revit versions have RevitCortex installed.
2. Install the .NET SDK if missing.
3. Back up the current installation to the Desktop.
4. Build and install with `deploy.ps1`.

The installer's messages follow the Windows language (English or Spanish). To build by hand, see "Building from Source" in the [original README](docs/REVITCORTEX_README.md#building-from-source).

## Status

- The Revit 2027 fix comes from a real case: the Revit 2027 journal showed the message above and the plugin didn't load.
- Autopilot's session, auto-save and router logic is covered by unit tests. The Revit-dependent parts (ribbon toggle, pop-up handling, saving on `Idling`) and the new interface need testing inside Revit.
- Revit 2023–2026 have not been tested with this fork.

If something breaks, the backup made by the installer includes instructions to roll back. Please report problems in this repository's issues.
