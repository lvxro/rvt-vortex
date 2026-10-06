using Autodesk.Revit.UI;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Commands;

/// <summary>
/// Shown by ribbon commands when the plugin did not finish starting. Gives
/// the real exception and the log path instead of a bare "not initialized".
/// </summary>
internal static class StartupGuard
{
    public static void ShowNotInitialized()
    {
        var error = RevitCortexApp.StartupError;
        var dialog = new TaskDialog(ForkInfo.ProductName)
        {
            MainInstruction = Localization.T("conn.not_initialized"),
            MainContent = error == null
                ? Localization.T("conn.not_initialized_unknown")
                : Localization.T("conn.not_initialized_detail", RevitCortexApp.StartupErrorLogPath),
            ExpandedContent = error ?? "",
            CommonButtons = TaskDialogCommonButtons.Close
        };
        dialog.Show();
    }
}
