# Pictures for the README

Nothing here is drawn by hand: each picture comes from the code, so it can be made again when a window changes.

| File | Where it comes from |
|---|---|
| `banner.svg` | Hand-written SVG. The arcs turn with a CSS animation, which stops for readers who ask for reduced motion. |
| `settings-*.png`, `powerbi-*.png`, `autopilot-panel.png`, `ribbon-icons.png` | The **UI preview** workflow, run by hand with language `en` (Actions → UI preview → Run workflow). It publishes the pictures to the `ui-previews` branch; these are copies of `settings-general-on-update`, `settings-tools`, `powerbi-data`, `powerbi-output`, `powerbi-data-schedules`, `pill-panel` and `ribbon-icons`. |
| `autopilot-pill-states.png`, `autopilot-summary.png`, `update-notice.png` | Crops of `pill-states`, `autopilot-summary` and `update-states` from the same run. |
| `showcase.png` | `powerbi-data`, `settings-general` and `autopilot-panel` laid over a gradient. |
| `installer.gif`, `installer-update.gif` | `distribution/tests/VortexUi.Demo.ps1 -Scenario install` / `update` with `-Language en`, recorded in a terminal. The demo plays the installer's real screens with sample data; nothing is installed. |

Pull requests render the previews in Spanish, so run the workflow by hand in English before copying pictures here.
