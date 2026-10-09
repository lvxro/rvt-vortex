using System.Text.RegularExpressions;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// The Revit-free part of send_code_to_revit's "preview" transaction mode: run the
/// script for real, read its result, then roll everything back.
///
/// How the rollback is done depends on the script. One that leaves transactions to the
/// tool is wrapped in a Transaction that is rolled back. One that opens its own cannot
/// run inside another Transaction (Revit refuses to nest them), so it runs inside a
/// TransactionGroup instead, and rolling the group back undoes what it committed.
/// </summary>
public static class ScriptPreview
{
    public const string Mode = "preview";

    public const string Note =
        "Preview: the script ran and every change to the model was rolled back. " +
        "Elements it created no longer exist, so their IDs are not valid. " +
        "Anything it did outside the model (files written, selection, zoom) was not undone.";

    private static readonly Regex OwnTransaction = new Regex(
        @"\bnew\s+(?:Autodesk\s*\.\s*Revit\s*\.\s*DB\s*\.\s*)?Transaction(?:Group)?\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// True when the source creates a Transaction or a TransactionGroup. A text check:
    /// if it guesses wrong the script fails with Revit's own error and the rollback still
    /// happens, so a wrong guess never changes the model.
    /// </summary>
    public static bool OpensOwnTransactions(string? code)
    {
        return !string.IsNullOrEmpty(code) && OwnTransaction.IsMatch(code!);
    }
}
