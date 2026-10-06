# RVT Vortex Autopilot: let the AI keep working in Revit while you are away

Give the AI a long task, walk away, and come back to the work done. If something went wrong, the log tells you exactly what happened.

Two things can stall the work while nobody is watching:

1. **Revit**: RevitCortex confirmation dialogs and Revit's own pop-ups that wait for a click.
2. **The AI client** (Claude Desktop, Claude Code…): it asks for permission before using a tool.

Autopilot solves the first one. Section 2 covers the second.

---

## 1. In Revit: the Autopilot button

The **RVT Vortex** ribbon panel has two large toggle buttons, both **grey when off and orange when on**:

| Button | What it does |
|---|---|
| **Vortex Switch** | Starts/stops the server the AI talks to |
| **Autopilot** | Lets the AI keep working without anyone clicking |

Clicking **Autopilot** asks for confirmation once. Until you turn it off:

| Situation | What happens |
|---|---|
| A tool asks to confirm an ordinary edit (delete, rename, change parameters…) | Approved automatically |
| A C# script (`send_code_to_revit`) | Depends on the **"Also allow C# scripts"** box in the start dialog. Ticked: runs without asking. Not ticked: **declined** instead of waiting, and the AI carries on with standard tools |
| A Revit pop-up appears | Closed automatically, choosing Close / Cancel / No; OK only when the pop-up offers nothing else |
| The model changes | **Saved automatically** as soon as Revit is idle (see below) |
| The document is closed | Autopilot turns off |
| Another document is opened | Stops auto-approving: from then on anything that needs a confirmation is declined, so a model you didn't pick is never edited |

To turn it off, click **Autopilot** again (it reads *Autopilot ON* while active) or click **Stop** in the small status pill at the bottom of the screen (drag it anywhere). The pill also shows the last automatic decision or save; hover it for a short explanation.

### Automatic saving

After each change to the model, the plugin saves as soon as Revit is idle. If the AI makes many changes in a row, they are batched: **at most one save per minute**, so heavy models don't spend more time saving than working. The last change is never left unsaved: it waits for the minute to pass and then saves.

Each save also leaves a **Revit backup copy** next to the file (`MyModel.0001.rvt`, `MyModel.0002.rvt`…). If a step went wrong, open the previous copy. The number of copies Revit keeps is set in *Save As → Options → Maximum backups*; raise it for long tasks.

Conditions:
- The model must have been saved at least once (it needs a file). The start dialog warns you if it hasn't.
- Read-only models are not saved.
- In workshared models the local file is saved; **it does not synchronize with central**.

### The log: what happened while you were away

Everything decided without you is written to:

```
%USERPROFILE%\.revitcortex\autopilot.log
```

Example:

```
2026-10-05 20:31:02  -------------------- AUTOPILOT ON --------------------
2026-10-05 20:33:15  APPROVED automatically: delete (12 element(s))
2026-10-05 20:33:16  SAVED (Cafe) after: delete_element
2026-10-05 20:35:40  REVIT DIALOG closed (TaskDialog, id 'TaskDialog_...', answer: Close): ...
2026-10-05 20:41:07  DECLINED (needs your confirmation): execute C# script (1 element(s))
```

The full technical history of every tool call is still in `audit.jsonl`, including the code of each C# script that ran (first 500 characters).

### Language

The ribbon, dialogs, floating window and log follow the **Windows display language**: English, Spanish or Italian (other languages fall back to English).

---

## 2. In the AI client: stop asking for permission at every step

### Claude Code (recommended for long tasks)

Create or edit `.claude/settings.json` in your working folder:

```json
{
  "permissions": {
    "allow": ["mcp__revitcortex"]
  }
}
```

This allows every RevitCortex tool without asking. For finer control, list individual tools instead, e.g. `mcp__revitcortex__get_project_info`.

### Claude Desktop

The first time the AI uses each tool, choose **"Always allow"**. Before leaving, run a short test (have it read the model, create something small and delete it) so the tools it will need are already approved.

**Ask the AI to use only RevitCortex tools.** Screen control (screenshots, clicks) and the browser trigger their own permission prompts and are never needed to work in Revit — RevitCortex talks to the model directly. The prompt below includes that rule.

---

## 3. Before you leave

- **Save a copy of the model** (Save As…).
- Turn on **Vortex Switch**, then **Autopilot**.
- Set Windows **not to sleep** (Settings → System → Power → Never). If the PC sleeps, the AI is cut off.
- Leave a floor plan or 3D view open, not a sheet: several tools don't work on sheets.

## 4. A prompt that works well

The AI keeps going as long as it has clear steps. If it hesitates halfway, it stops to ask — and waits. Give it everything up front:

```
I'm leaving and won't be around to answer. Work in Revit with RevitCortex on your own until you finish, without asking me anything. Autopilot is on.

TASK:
[Describe what you want in as much detail as possible: model, levels, views, families, quantities, names, criteria. Number the parts if there are several.]

HOW TO WORK:
0. Use ONLY RevitCortex tools. Do not use screen control (screenshots, clicks, keyboard) or the browser: they are not needed and they ask for permissions I won't be around to give.
1. Understand the model first with ONE full get_project_info call. Make a short plan and follow it.
2. Before editing many elements, run dryRun: true, read only modifiedCount/skippedCount, then run with dryRun: false.
3. If a step fails or comes back "cancelled", don't stop: try one alternative with other tools; if that fails too, note it as pending and move on.
4. Don't guess parameter or category names: check a sample element first.
5. After each major part, verify with a small query (1 or 2 sample elements), not by re-reading everything.
6. If useful, you may use send_code_to_revit: short scripts, one at a time, and list each one in the summary.

SAVE TOKENS:
- Don't re-request information you already have in this conversation.
- After the first call, use get_project_info with includeLevels/includeLinks/includePhases/includeWorksets set to false.
- Use compact: true and summaryOnly: true whenever the tool accepts them.
- Always filter: categories, parameterNames, maxElements, maxRows, maxWarnings: 10. Never pull full lists when a count is enough.
- Prefer the most specific tool (check_model_health before workflow_model_audit; export_elements_data with a filter before ai_element_filter).
- Batch changes: one call for many elements (bulk_modify_parameter_values, sync_csv_parameters) instead of one per element.
- Don't narrate between steps or repeat results. Write only what you need to move forward.

WHEN YOU FINISH, give me a short summary:
- What you did (with quantities).
- What failed or is pending, and why.
- C# scripts you ran, if any.
- What I should check by hand.
```

Remove item 6 if you didn't allow C# scripts. The most effective token saver is the task itself: concrete names ("level L2", "family Single Door 0.90") spare the AI lookups. For very large jobs, split them across conversations: every Revit response stays in the conversation and makes each new step more expensive.

## Limits

- If Revit hangs or crashes, nothing can keep it going.
- A pop-up that accepts no automatic answer is logged as "could NOT be closed". Rare, but possible.
- Autopilot doesn't make the AI smarter: a badly framed task will be done badly, just without asking. That's what the backup copy is for.
