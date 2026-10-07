using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Security;

/// <summary>
/// The "Report a bug" package is attached to a public GitHub issue, so these
/// tests pin what must never survive redaction: tool inputs, script source,
/// paths, names and ids.
/// </summary>
public class SupportReportRedactorTests
{
    private static string AuditLine(string tool, string? errorMessage = null)
    {
        var entry = new JObject
        {
            ["ts"] = "2026-10-06T22:12:07.1230000Z",
            ["v"] = 2,
            ["tool"] = tool,
            ["input_summary"] = "outputDirectory=C:\\Users\\mario.rossi\\Desktop\\TorreA, name=Planta Baja",
            ["result"] = errorMessage == null ? "ok" : "fail",
            ["elements_affected"] = 0,
            ["duration_ms"] = 42,
            ["response_bytes"] = 1234,
            ["code_snippet"] = "var secret = \"TorreA\";",
            ["code_hash"] = "abc123",
        };
        if (errorMessage != null)
        {
            entry["error_code"] = "Unknown";
            entry["error_message"] = errorMessage;
        }
        return entry.ToString(Newtonsoft.Json.Formatting.None);
    }

    [Fact]
    public void RedactAuditLine_DropsInputsAndScriptSource_KeepsDiagnostics()
    {
        var redacted = SupportReportRedactor.RedactAuditLine(AuditLine("export_families"));

        Assert.NotNull(redacted);
        var obj = JObject.Parse(redacted!);
        Assert.Null(obj["input_summary"]);
        Assert.Null(obj["code_snippet"]);
        Assert.DoesNotContain("mario", redacted);
        Assert.DoesNotContain("TorreA", redacted);
        Assert.DoesNotContain("Planta", redacted);

        Assert.Equal("export_families", (string?)obj["tool"]);
        Assert.Equal("ok", (string?)obj["result"]);
        Assert.Equal(42, (long)obj["duration_ms"]!);
        Assert.Equal(1234, (long)obj["response_bytes"]!);
        Assert.Equal("abc123", (string?)obj["code_hash"]);
    }

    [Fact]
    public void RedactAuditLine_KeepsTimestampTextUnchanged()
    {
        var redacted = SupportReportRedactor.RedactAuditLine(AuditLine("say_hello"));

        Assert.Contains("\"ts\":\"2026-10-06T22:12:07.1230000Z\"", redacted);
    }

    [Fact]
    public void RedactAuditLine_ScrubsPathsAndQuotedNamesFromErrorMessage()
    {
        var line = AuditLine("ifc_open_or_import",
            "Failed to open 'Torre A.ifc' at C:\\Users\\mario.rossi\\Docs\\TorreA.ifc for element 606873");

        var redacted = SupportReportRedactor.RedactAuditLine(line);

        Assert.NotNull(redacted);
        var message = (string?)JObject.Parse(redacted!)["error_message"];
        Assert.False(string.IsNullOrEmpty(message));
        Assert.DoesNotContain("mario", message);
        Assert.DoesNotContain("Torre", message, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("606873", message);
        Assert.Contains("failed to open", message);
        Assert.Equal("Unknown", (string?)JObject.Parse(redacted!)["error_code"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void RedactAuditLine_ReturnsNullForBlankOrUnparseableLines(string? line)
    {
        Assert.Null(SupportReportRedactor.RedactAuditLine(line));
    }

    [Fact]
    public void RedactAuditLine_DropsUnknownKeysAndNestedValuesUnderKnownKeys()
    {
        var line = "{\"tool\":{\"name\":\"TorreA\"},\"result\":\"ok\",\"model_name\":\"TorreA\"}";

        var redacted = SupportReportRedactor.RedactAuditLine(line);

        Assert.NotNull(redacted);
        Assert.DoesNotContain("TorreA", redacted);
        Assert.Equal("ok", (string?)JObject.Parse(redacted!)["result"]);
    }

    [Fact]
    public void RedactAudit_KeepsOnlyTheMostRecentLines()
    {
        var input = new StringReader(string.Join("\n", new[]
        {
            AuditLine("tool_one"), AuditLine("tool_two"), "", "garbage",
            AuditLine("tool_three"), AuditLine("tool_four"),
        }));
        var output = new StringWriter();

        var written = SupportReportRedactor.RedactAudit(input, output, maxLines: 2);

        var text = output.ToString();
        Assert.Equal(2, written);
        Assert.DoesNotContain("tool_one", text);
        Assert.DoesNotContain("tool_two", text);
        Assert.Contains("tool_three", text);
        Assert.Contains("tool_four", text);
        Assert.DoesNotContain("mario", text);
    }

    [Fact]
    public void RedactSettings_KeepsOnlyAllowlistedKeys()
    {
        var settings = new JObject
        {
            ["Port"] = 8080,
            ["ReadOnlyMode"] = true,
            ["DisabledTools"] = new JArray("purge_unused"),
            ["InstallationId"] = "3f2c1d7e-0000-4000-8000-123456789abc",
            ["PowerBiWorkspaceId"] = "tenant-secret",
            ["SomeFutureKey"] = "C:\\Users\\mario.rossi",
        }.ToString();

        var redacted = JObject.Parse(SupportReportRedactor.RedactSettings(settings));

        Assert.Equal(8080, (int)redacted["Port"]!);
        Assert.True((bool)redacted["ReadOnlyMode"]!);
        Assert.Equal("purge_unused", (string?)redacted["DisabledTools"]![0]);
        Assert.Null(redacted["InstallationId"]);
        Assert.Null(redacted["PowerBiWorkspaceId"]);
        Assert.Null(redacted["SomeFutureKey"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ this is not json")]
    public void RedactSettings_ReturnsEmptyObjectForUnreadableInput(string? settings)
    {
        var redacted = JObject.Parse(SupportReportRedactor.RedactSettings(settings));

        Assert.Empty(redacted.Properties());
    }

    [Theory]
    [InlineData("{\"SupportReportIncludePrivateData\":true}", true)]
    [InlineData("{\"SupportReportIncludePrivateData\":false}", false)]
    [InlineData("{\"SupportReportIncludePrivateData\":\"true\"}", false)]
    [InlineData("{\"SupportReportIncludePrivateData\":1}", false)]
    [InlineData("{\"Port\":8080}", false)]
    [InlineData("{ this is not json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IncludesPrivateData_IsTrueOnlyForAnExplicitBooleanTrue(string? settings, bool expected)
    {
        Assert.Equal(expected, SupportReportRedactor.IncludesPrivateData(settings));
    }
}
