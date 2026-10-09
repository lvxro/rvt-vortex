using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Saves the active document to its file, with no dialog, and reports whether it ended
/// up saved. For when the user asks to save: the AI must not call it on its own.
///
/// It saves the file the document was opened from. On a workshared model that is the
/// local file: it never synchronizes with central. It follows the same rules as
/// Autopilot's auto-save (RevitCortexApp.OnIdlingAutoSave): a read-only document and one
/// that was never saved are left alone, and a document with no changes is not rewritten.
/// </summary>
[ToolSafety(false, false)]
public class SaveDocumentTool : ICortexTool
{
    public string Name => "save_document";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Saves the active document to its file, with no dialog. Never synchronizes with central.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (doc == null) return error!;

        try
        {
            if (doc.IsReadOnly)
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    "The document is read-only and cannot be saved",
                    suggestion: "Tell the user: to keep the changes they need File > Save As in Revit");

            if (string.IsNullOrEmpty(doc.PathName))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "The document has never been saved, so it has no file to save to",
                    suggestion: "Tell the user to save it once from Revit (File > Save As); after that this tool can save it");

            if (doc.IsModifiable)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "A transaction is open in the document, so it cannot be saved now",
                    suggestion: "Let the current operation finish, then call save_document again");

            var hadUnsavedChanges = doc.IsModified;
            if (hadUnsavedChanges)
                doc.Save();

            var saved = !doc.IsModified;
            string message;
            if (!hadUnsavedChanges)
                message = "Nothing to save: the document has no unsaved changes";
            else if (!saved)
                message = "Revit ran the save but still reports unsaved changes";
            else if (doc.IsWorkshared)
                message = "Saved the local file. Not synchronized with central";
            else
                message = "Saved";

            return CortexResult<object>.Ok(new
            {
                saved,
                hadUnsavedChanges,
                isModified = doc.IsModified,
                title = doc.Title,
                path = doc.PathName,
                isWorkshared = doc.IsWorkshared,
                message
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Could not save the document: {ex.Message}",
                suggestion: "Do not retry in a loop. Tell the user the save failed and why");
        }
    }
}
