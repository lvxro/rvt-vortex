# Changelog

What changed in RVT Vortex, release by release. Versions start at 1.1.0, above the 1.0.x of [RevitCortex](https://github.com/LuDattilo/revitcortex), the project this fork is built on. The reasoning behind the larger changes is in [FORK.md](FORK.md).

## Unreleased

On `main`, in the next release.

### Interface

- **One dark theme** for every window: Settings, the update notice, the Autopilot pill and its summary, and the Power BI export share one palette and one set of controls. ([#2](https://github.com/lvxro/rvt-vortex/pull/2))
- **Settings** in two tabs. *General*: each setting is a row with its explanation, and the server can be started and stopped from here. *Tools*: a search box, an enabled / disabled filter and a switch per category.
- **Update notice** redesigned: what's new, download progress, ready to install, and a retry when the download fails.
- **Autopilot pill**: its mark spins while the AI is calling tools; it shows how many edits were approved and declined, the latest event and the last save, and an amber warning when nothing can happen. An arrow opens an activity panel with totals and the last events.
- **Autopilot summary** when Autopilot stops: how long it ran, the totals and the steps it declined.
- **Power BI export** rebuilt in two steps. ([#3](https://github.com/lvxro/rvt-vortex/pull/3))
  - *Data*: scope cards (whole model, active view, current selection, existing schedules), categories in tabs, parameters with a filter, and the list of CSV columns with move up / down.
  - *Output*: folder and file name, overwrite, export on save, the link with Power BI, a preview of the first rows, and the column types.
  - Profiles moved to a menu at the top right; loading one restores its columns in order.
  - The whole window comes in English, Spanish and Italian (its text was hard-coded in Italian).
- **Installer**: `install.bat`, the in-app update and the uninstaller open on a spinning ASCII vortex, list their steps with a check mark each, show a progress bar while the server is copied, and end with a summary. During an update the vortex turns while Revit closes, and the window closes by itself. In English, Spanish or Italian. ([#4](https://github.com/lvxro/rvt-vortex/pull/4))

### Changed

- **CSV column order.** The Power BI export writes the columns in the order of the window's list, instance parameters first and type parameters after them. `push_to_powerbi` follows the order of `parameterNames`. Before, the order was whatever Revit enumerated.
- Categories hidden by the chosen scope are no longer exported.

### Fixed

- A failed install now stays on screen with the reason, instead of closing with the error in it.

### Under the hood

- **UI preview tests** build every window without Revit, in each state a user can meet; CI publishes the pictures.
- The installer scripts are tested under Windows PowerShell 5.1, and CI installs, updates and uninstalls the built package.

## 1.1.3 — 2026-10-07

Privacy. ([#1](https://github.com/lvxro/rvt-vortex/pull/1))

- **"Report a bug" is redacted by default.** The ZIP no longer includes your user name, machine name, model names, paths, tool inputs, script source or the Revit journal, and the public issue carries only version numbers.
- **Error telemetry is off.** Nothing is queued or sent, Revit no longer asks about it on first run, and the toggle is hidden in Settings.
- The docs no longer describe the C# script filter as a sandbox: it is a text check, not isolation.
- A release is no longer published when a Revit version fails to build; CI runs the unit tests on every push and pull request.

## 1.1.2 — 2026-10-06

- **Own add-in ID.** The original ID was a placeholder that Autodesk's FormaOpenIn add-in (shipped with Revit 2027) also uses; with both installed, the ribbon buttons answered "plugin not initialized". The installers also remove a stale manifest from the Revit 2027 all-users folder.
- **No licensing.** The "Premium" branding and the License & Account button are gone, and the licensing code inherited from the original is never started.
- **Autopilot pill** redesigned as a compact dark pill.
- `set_element_parameters` has a dry run.
- Models in Spanish and Portuguese are detected.
- When the plugin fails to start, the buttons show the real error instead of "not initialized".

## 1.1.1 — 2026-10-06

- The installer stops a running MCP server before replacing its files, so an update no longer fails on locked files.
- Installer messages in English, with RVT Vortex titles.
- The release workflow attaches the ZIP to a release created from the GitHub web page.

## 1.1.0 — 2026-10-06

The first release of the fork.

- **Autopilot.** A ribbon toggle that lets the AI keep working without anyone clicking: ordinary confirmations are approved, C# scripts are declined unless allowed at start, Revit's pop-ups are closed, the model is saved when Revit is idle (at most once a minute), and everything is written to `autopilot.log`. Anything that needs a human decision is declined, never approved.
- **Revit 2027.** The add-in installs to the per-user folder that Revit 2027 loads; the original installers left no valid copy.
- **Ribbon.** Two large toggles (Vortex Switch, Autopilot), grey when off and orange when on.
- **Languages.** The interface follows the Windows display language: English, Spanish or Italian. Spanish was added to every existing text.
- **Guidance for the AI.** The usage rules are sent to every MCP client on connect; guidance that had drifted from the code is fixed (`ai_element_filter`, `operate_element`, parameter filters, stale defaults).
- **RVT Vortex name** everywhere the user looks. Internal names (the `.revitcortex` folder, the `revitcortex` server entry, tool names) are unchanged, so existing setups keep working.
- **Updates and bug reports point to this repository.** "Report a bug" builds a diagnostic ZIP and opens an issue here; releases are built by GitHub Actions.
