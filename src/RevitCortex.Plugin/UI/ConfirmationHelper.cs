using System;
using Autodesk.Revit.UI;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Shows a native Revit TaskDialog before destructive/bulk operations.
/// Called inside tool Execute() after parameter validation but BEFORE opening Transaction.
/// </summary>
public static class ConfirmationHelper
{
    /// <summary>
    /// Shows a confirmation dialog for destructive operations.
    /// </summary>
    /// <param name="action">Action verb: "delete", "purge", "rename", "modify", etc.</param>
    /// <param name="elementCount">Number of elements affected.</param>
    /// <param name="description">Optional description of what the operation will do.</param>
    /// <returns>true = Yes, false = No, null = Yes to All.</returns>
    public static bool? Confirm(string action, int elementCount, string? description)
    {
        if (elementCount <= 0) return true; // Nothing to do

        var dialog = new TaskDialog(Localization.T("confirm.title"))
        {
            MainInstruction = Localization.T("confirm.instruction", action, elementCount),
            CommonButtons = TaskDialogCommonButtons.None
        };

        if (!string.IsNullOrEmpty(description))
            dialog.MainContent = description;

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Localization.T("confirm.yes"),
            Localization.T("confirm.yes_desc"));
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Localization.T("confirm.yes_all"),
            Localization.T("confirm.yes_all_desc"));
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, Localization.T("confirm.auto"),
            Localization.T("confirm.auto_desc"));
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, Localization.T("confirm.no"),
            Localization.T("confirm.no_desc"));

        var result = ShowOwn(dialog);
        if (result == TaskDialogResult.CommandLink2) return null;  // Yes to All
        if (result == TaskDialogResult.CommandLink1) return true;  // Yes
        if (result == TaskDialogResult.CommandLink3) return AutoSentinel; // Auto
        return false; // No (or closed)
    }

    /// <summary>
    /// True while one of RevitCortex's own TaskDialogs is on screen. The
    /// unattended dialog handler checks it so it never auto-dismisses our own
    /// confirmations (those are resolved in CortexSession instead).
    /// </summary>
    public static bool IsShowingOwnDialog => _ownDialogDepth > 0;
    private static int _ownDialogDepth;

    private static TaskDialogResult ShowOwn(TaskDialog dialog)
    {
        System.Threading.Interlocked.Increment(ref _ownDialogDepth);
        try
        {
            return dialog.Show();
        }
        finally
        {
            System.Threading.Interlocked.Decrement(ref _ownDialogDepth);
        }
    }

    /// <summary>
    /// Sentinel value returned by Confirm() when the user clicks "Auto".
    /// CortexSession.RequestConfirmation checks for this value and sets AutoMode.
    /// </summary>
    public const bool AutoSentinel = true;

    /// <summary>
    /// Variant wired to a CortexSession: sets session.AutoMode = true when Auto is clicked
    /// and fires AutoModeChanged so the ribbon can update its button visibility immediately.
    /// This overload is used by RevitCortexApp instead of the bare Confirm delegate.
    /// </summary>
    public static bool? ConfirmWithSession(string action, int elementCount, string? description,
        CortexSession session)
    {
        if (elementCount <= 0) return true;

        var dialog = new TaskDialog(Localization.T("confirm.title"))
        {
            MainInstruction = Localization.T("confirm.instruction", action, elementCount),
            CommonButtons = TaskDialogCommonButtons.None
        };

        if (!string.IsNullOrEmpty(description))
            dialog.MainContent = description;

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Localization.T("confirm.yes"),
            Localization.T("confirm.yes_desc"));
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Localization.T("confirm.yes_all"),
            Localization.T("confirm.yes_all_desc"));
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, Localization.T("confirm.auto"),
            Localization.T("confirm.auto_desc"));
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, Localization.T("confirm.no"),
            Localization.T("confirm.no_desc"));

        var result = ShowOwn(dialog);
        if (result == TaskDialogResult.CommandLink2) return null;  // Yes to All
        if (result == TaskDialogResult.CommandLink1) return true;  // Yes
        if (result == TaskDialogResult.CommandLink3)
        {
            session.AutoMode = true;
            AutoModeChanged?.Invoke(true);
            return true; // proceed with current operation
        }
        return false; // No (or closed)
    }

    /// <summary>
    /// Shows a confirmation dialog for critical operations. Batch approvals are
    /// intentionally unavailable here because these actions require an explicit
    /// per-operation decision.
    /// </summary>
    public static bool? ConfirmCritical(string action, int elementCount, string? description)
    {
        if (elementCount <= 0) return true;

        var dialog = new TaskDialog(Localization.T("confirm.critical_title"))
        {
            MainInstruction = Localization.T("confirm.instruction", action, elementCount),
            CommonButtons = TaskDialogCommonButtons.None
        };

        if (!string.IsNullOrEmpty(description))
            dialog.MainContent = description;

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Localization.T("confirm.yes"),
            Localization.T("confirm.yes_only_desc"));
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Localization.T("confirm.no"),
            Localization.T("confirm.no_desc"));

        var result = ShowOwn(dialog);
        if (result == TaskDialogResult.CommandLink1) return true;
        return false;
    }

    /// <summary>
    /// Fired when Auto mode is activated or deactivated via the confirmation dialog.
    /// The ribbon subscribes to this to show/hide the "Stop Auto" button immediately.
    /// </summary>
    public static event Action<bool>? AutoModeChanged;

    /// <summary>
    /// Raises AutoModeChanged. Call this from outside ConfirmationHelper (e.g. StopAutoMode command).
    /// </summary>
    public static void NotifyAutoModeChanged(bool active) => AutoModeChanged?.Invoke(active);

    /// <summary>
    /// Returns a standard cancelled response for CortexResult.
    /// </summary>
    public static Core.Results.CortexResult<object> CancelledResult()
    {
        return Core.Results.CortexResult<object>.Fail(
            Core.Results.CortexErrorCode.Cancelled,
            "Operation cancelled by user",
            suggestion: "The user declined the confirmation dialog. Ask if they want to retry.");
    }
}
