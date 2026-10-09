using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Autodesk.Revit.DB;
using RevitCortex.Core.Results;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// Runs a compiled script in "preview": for real, inside a TransactionGroup that is
/// always rolled back. Shared by both executors (Roslyn on Revit 2025+, CodeDom before).
///
/// The result is serialized BEFORE the rollback: an element the script created stops
/// being readable the moment its creation is undone.
/// </summary>
public static class ScriptPreviewRunner
{
    /// <param name="document">The document the script works on.</param>
    /// <param name="code">The script's source, to tell whether it opens its own transactions.</param>
    /// <param name="invoke">Calls the compiled script and returns what it returned.</param>
    /// <param name="serialize">The executor's own result serializer.</param>
    public static CortexResult<object> Run(
        Document document, string code, Func<object?> invoke, Func<object?, object> serialize)
    {
        object? preview = null;
        Exception? scriptError = null;
        string? rollbackError = null;

        var group = new TransactionGroup(document, "RevitCortex: Script Preview");
        try
        {
            group.Start();
            try
            {
                if (ScriptPreview.OpensOwnTransactions(code))
                {
                    preview = serialize(invoke());
                }
                else
                {
                    using var tx = new Transaction(document, "RevitCortex: Script Preview");
                    TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();
                    try
                    {
                        preview = serialize(invoke());
                    }
                    finally
                    {
                        if (tx.GetStatus() == TransactionStatus.Started)
                            tx.RollBack();
                    }
                }
            }
            catch (Exception ex)
            {
                scriptError = ex;
            }

            if (group.GetStatus() == TransactionStatus.Started)
            {
                try { group.RollBack(); }
                catch (Exception ex) { rollbackError = ex.Message; }
            }
        }
        finally
        {
            try { group.Dispose(); }
            catch { /* reported below through rollbackError */ }
        }

        // The one outcome where the model may have changed: say so, never "nothing changed".
        if (rollbackError != null)
        {
            var scriptPart = scriptError == null
                ? string.Empty
                : $" The script also failed: {Unwrap(scriptError).Message}";
            return CortexResult<object>.Fail(
                CortexErrorCode.TransactionFailed,
                $"The preview could not be rolled back: {rollbackError}{scriptPart}",
                suggestion: "The script most likely left a transaction open. The model MAY have changed: tell the user to check it and use Undo in Revit if needed. " +
                            "In the script, put every Transaction in a using block and Commit or RollBack it.");
        }

        // A failed script is reported by the executor exactly as in the other modes.
        if (scriptError != null)
            ExceptionDispatchInfo.Capture(scriptError).Throw();

        return CortexResult<object>.Ok(preview!);
    }

    private static Exception Unwrap(Exception ex)
    {
        return ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
    }
}
