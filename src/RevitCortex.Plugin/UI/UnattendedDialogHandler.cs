using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// While Autopilot (Unattended mode) is on, dismisses Revit's own modal dialogs (warnings,
/// info boxes, "are you sure" prompts raised outside a Transaction) that would
/// otherwise freeze Revit — and every queued MCP call — until someone clicks.
///
/// Write tools already route Transaction warnings through
/// TransactionFailureHandling.SuppressWarnings; this covers everything else.
///
/// Policy is "fail closed": it prefers the answer that does NOT approve
/// anything (Close / Cancel / No) and only falls back to OK when the dialog
/// offers nothing else (typically a purely informational box). Every dismissal
/// is written to <see cref="UnattendedLog"/>.
///
/// RevitCortex's own confirmation dialogs are never touched (see
/// <see cref="ConfirmationHelper.IsShowingOwnDialog"/>).
/// </summary>
public sealed class UnattendedDialogHandler
{
    // Win32 / TaskDialog result codes accepted by DialogBoxShowingEventArgs.OverrideResult.
    private const int IdOk = 1;
    private const int IdCancel = 2;
    private const int IdNo = 7;
    private const int IdClose = 8;

    private readonly Func<CortexSession?> _getSession;

    /// <summary>
    /// Raised (on the UI thread) after a Revit dialog was dismissed, with the
    /// localized name of the answer that was chosen ("Cancel", "Close"...).
    /// Lets the Autopilot pill count dismissed pop-ups.
    /// </summary>
    public event Action<string>? DialogDismissed;

    public UnattendedDialogHandler(Func<CortexSession?> getSession)
    {
        _getSession = getSession;
    }

    public void Attach(UIControlledApplication application)
    {
        application.DialogBoxShowing += OnDialogBoxShowing;
    }

    public void Detach(UIControlledApplication application)
    {
        application.DialogBoxShowing -= OnDialogBoxShowing;
    }

    private void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs args)
    {
        try
        {
            var session = _getSession();
            if (session == null || !session.UnattendedMode) return;
            if (ConfirmationHelper.IsShowingOwnDialog) return;

            string kind;
            string message;
            int[] order;

            if (args is TaskDialogShowingEventArgs td)
            {
                kind = "TaskDialog";
                message = td.Message;
                order = new[] { IdClose, IdCancel, IdNo, IdOk };
            }
            else if (args is MessageBoxShowingEventArgs mb)
            {
                kind = "MessageBox";
                message = mb.Message;
                order = new[] { IdCancel, IdNo, IdOk };
            }
            else
            {
                kind = "Dialog";
                message = "";
                order = new[] { IdCancel, IdClose, IdOk };
            }

            foreach (var code in order)
            {
                if (args.OverrideResult(code))
                {
                    var answer = Describe(code);
                    UnattendedLog.Write(Localization.T("log.dialog_closed",
                        kind, args.DialogId, answer, UnattendedLog.Shorten(message)));
                    try { DialogDismissed?.Invoke(answer); } catch { /* status only */ }
                    return;
                }
            }

            UnattendedLog.Write(Localization.T("log.dialog_failed",
                kind, args.DialogId, UnattendedLog.Shorten(message)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Unattended dialog handler error: {ex.Message}");
        }
    }

    private static string Describe(int code)
    {
        switch (code)
        {
            case IdOk: return Localization.T("log.answer_ok");
            case IdCancel: return Localization.T("log.answer_cancel");
            case IdNo: return Localization.T("log.answer_no");
            case IdClose: return Localization.T("log.answer_close");
            default: return code.ToString();
        }
    }
}
