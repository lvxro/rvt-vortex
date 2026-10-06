using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Plugin.UI;
using System;

namespace RevitCortex.Plugin.Commands;

/// <summary>
/// Ribbon toggle "Autopilot": lets the AI keep working while the user is away.
///
/// Off → shows one confirmation dialog, then turns on Auto + Unattended mode:
/// ordinary write confirmations are auto-approved, critical ones
/// (send_code_to_revit) are declined instead of waiting for a click (unless the
/// user ticks the opt-in box), Revit's own modal dialogs are dismissed
/// (fail closed), the model is saved after changes and everything is logged.
///
/// On → turns it off immediately (no dialog).
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class ToggleAutopilot : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var app = RevitCortexApp.Instance;
            var session = app?.Session;
            if (app == null || session == null)
            {
                message = Localization.T("conn.not_initialized");
                return Result.Failed;
            }

            if (session.UnattendedMode)
            {
                app.StopAutopilot("log.stop");
                return Result.Succeeded;
            }

            var dialog = new TaskDialog(Localization.T("ap.title"))
            {
                MainInstruction = Localization.T("ap.instruction"),
                MainContent = Localization.T("ap.content", UnattendedLog.FilePath),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
                VerificationText = Localization.T("ap.verification")
            };

            var footer = "";
            if (!app.IsServiceRunning)
                footer += Localization.T("ap.footer_server_off") + " ";
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc != null && string.IsNullOrEmpty(doc.PathName))
                footer += Localization.T("ap.footer_unsaved");
            if (footer.Length > 0)
                dialog.FooterText = footer.Trim();

            if (dialog.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            app.StartAutopilot(dialog.WasVerificationChecked());
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
