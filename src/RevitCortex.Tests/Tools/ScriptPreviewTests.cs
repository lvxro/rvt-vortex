using System.IO;
using RevitCortex.Tools.CodeExecution;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// send_code_to_revit's "preview" mode runs a script and rolls every change back.
/// A script that opens its own transactions cannot be wrapped in another one (Revit
/// does not nest them), so the mode has to tell the two kinds of script apart.
/// </summary>
public class ScriptPreviewTests
{
    [Theory]
    [InlineData("using (var t = new Transaction(document, \"x\")) { t.Start(); }")]
    [InlineData("var g = new TransactionGroup(document);")]
    [InlineData("var t = new  Transaction (document);")]
    [InlineData("var t = new Autodesk.Revit.DB.Transaction(document, \"x\");")]
    public void ScriptThatOpensItsOwnTransactions_IsRecognised(string code)
    {
        Assert.True(ScriptPreview.OpensOwnTransactions(code));
    }

    [Theory]
    [InlineData("return document.Title;")]
    [InlineData("var s = new SubTransaction(document);")]      // only valid inside a transaction the tool opens
    [InlineData("var st = new TransactionStatus();")]
    [InlineData("tx.Commit(); renew Transaction(x);")]
    [InlineData("")]
    [InlineData(null)]
    public void ScriptThatLeavesTransactionsToTheTool_IsRecognised(string? code)
    {
        Assert.False(ScriptPreview.OpensOwnTransactions(code));
    }

    [Fact]
    public void Note_SaysWhatIsAndIsNotUndone()
    {
        Assert.Contains("rolled back", ScriptPreview.Note);
        Assert.Contains("IDs are not valid", ScriptPreview.Note);
        Assert.Contains("outside the model", ScriptPreview.Note);
    }

    private static string ReadTools(params string[] parts)
    {
        var all = new string[parts.Length + 5];
        all[0] = all[1] = all[2] = all[3] = "..";
        all[4] = "RevitCortex.Tools";
        parts.CopyTo(all, 5);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(all)));
    }

    [Theory]
    [InlineData("RoslynExecutor.cs")]   // Revit 2025+
    [InlineData("CodeDomExecutor.cs")]  // Revit 2023/2024
    public void BothExecutors_HandPreviewToTheSharedRunner(string file)
    {
        var source = ReadTools("CodeExecution", file);

        Assert.Contains("if (transactionMode == ScriptPreview.Mode)", source);
        Assert.Contains("return ScriptPreviewRunner.Run(globals.document, code,", source);
    }

    [Fact]
    public void Runner_ReadsTheResultBeforeUndoing_AndNeverCommits()
    {
        var source = ReadTools("CodeExecution", "ScriptPreviewRunner.cs");

        // An element created by the script cannot be read once its creation is undone.
        Assert.True(source.IndexOf("preview = serialize(invoke());") < source.IndexOf("group.RollBack();"));
        Assert.DoesNotContain(".Commit()", source);
        Assert.DoesNotContain(".Assimilate()", source);
        // A rollback that fails must not be reported as "nothing changed".
        Assert.Contains("The preview could not be rolled back", source);
    }

    [Fact]
    public void Tool_MarksAPreviewResult()
    {
        var source = ReadTools("Elements", "SendCodeToRevitTool.cs");

        Assert.Contains("data[\"preview\"] = true;", source);
        Assert.Contains("data[\"modelChanged\"] = false;", source);
    }
}
