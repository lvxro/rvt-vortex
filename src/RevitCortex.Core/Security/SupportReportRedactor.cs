using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Telemetry;

namespace RevitCortex.Core.Security;

/// <summary>
/// Strips private data from the files that go into a "Report a bug" package.
///
/// RVT Vortex asks the user to attach that package to a PUBLIC GitHub issue,
/// so by default it must not carry anything that identifies the person, the
/// machine or the project: no user or machine name, no model names, no file
/// paths, no tool inputs, no script source.
///
/// Both redactors are allowlists: a key that is not known to be safe is
/// dropped, so a field added to the audit log or to settings.json later stays
/// out of the package until someone decides it is safe.
///
/// Known limit: error messages are scrubbed best-effort (paths, GUIDs,
/// e-mails, quoted text, compound tokens and numbers are removed), but a bare
/// unquoted name inside a message can survive. That is why the plugin still
/// asks the user to look at the ZIP before attaching it.
///
/// No Revit dependency, so it is unit tested.
/// </summary>
public static class SupportReportRedactor
{
    /// <summary>
    /// settings.json key that turns redaction off (default false). With it on,
    /// the package also holds the full audit log, settings, usage data and the
    /// Revit journal, and the plugin no longer points the user at a public issue.
    /// </summary>
    public const string IncludePrivateDataSettingKey = "SupportReportIncludePrivateData";

    /// <summary>Only the most recent audit entries go into a redacted package.</summary>
    public const int MaxAuditLines = 5000;

    // Audit fields that describe WHAT ran and how it went, never what it ran on.
    // Deliberately absent: input_summary (parameter values, paths, names) and
    // code_snippet (script source). error_message is handled separately.
    private static readonly HashSet<string> SafeAuditKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "ts", "v", "tool", "result", "error_code", "elements_affected",
        "duration_ms", "response_bytes", "code_hash",
    };

    // Settings that explain plugin behavior. Deliberately absent: InstallationId,
    // Power BI workspace/dataset/tenant ids and anything not listed here.
    private static readonly HashSet<string> SafeSettingsKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Port", "LogLevel", "ReadOnlyMode", "EnableCodeExecution",
        "DisabledTools", "SupportReportKeepCount",
    };

    /// <summary>
    /// True only when settings.json explicitly sets
    /// <see cref="IncludePrivateDataSettingKey"/> to boolean true. Missing key,
    /// wrong type or unreadable JSON all mean "redact".
    /// </summary>
    public static bool IncludesPrivateData(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return false;
        try
        {
            var token = JObject.Parse(settingsJson!)[IncludePrivateDataSettingKey];
            return token != null && token.Type == JTokenType.Boolean && (bool)token;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Redacts one audit.jsonl line. Returns null for blank or unparseable
    /// lines (they are dropped: an unparseable line cannot be proven safe).
    /// </summary>
    public static string? RedactAuditLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        JObject entry;
        try { entry = LoadObject(line!); }
        catch { return null; }

        var safe = new JObject();
        foreach (var prop in entry.Properties())
        {
            if (!SafeAuditKeys.Contains(prop.Name)) continue;
            // Scalars only: a nested object or array under a known key is not
            // something the audit logger writes, so do not trust it.
            if (prop.Value is JValue value) safe[prop.Name] = value.DeepClone();
        }

        // Error messages often quote element names or paths. Normalize() drops
        // paths, GUIDs, e-mails, quoted text, compound tokens and numbers, which
        // keeps the shape of the message ("element _ does not exist") for triage.
        var message = entry["error_message"];
        if (message != null && message.Type == JTokenType.String)
        {
            var normalized = MessageSanitizer.Normalize((string?)message);
            if (normalized.Length > 0) safe["error_message"] = normalized;
        }

        return safe.ToString(Formatting.None);
    }

    // Parses without Json.NET's date detection, so the "ts" string is copied
    // through exactly as the audit logger wrote it instead of being re-formatted.
    private static JObject LoadObject(string json)
    {
        using (var reader = new JsonTextReader(new StringReader(json)))
        {
            reader.DateParseHandling = DateParseHandling.None;
            return JObject.Load(reader);
        }
    }

    /// <summary>
    /// Copies the last <paramref name="maxLines"/> entries of an audit log from
    /// <paramref name="input"/> to <paramref name="output"/>, redacting each
    /// one. Returns the number of lines written.
    /// </summary>
    public static int RedactAudit(TextReader input, TextWriter output, int maxLines = MaxAuditLines)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (maxLines <= 0) return 0;

        var tail = new Queue<string>();
        string? line;
        while ((line = input.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            tail.Enqueue(line);
            if (tail.Count > maxLines) tail.Dequeue();
        }

        int written = 0;
        foreach (var raw in tail)
        {
            var safe = RedactAuditLine(raw);
            if (safe == null) continue;
            output.WriteLine(safe);
            written++;
        }
        return written;
    }

    /// <summary>
    /// Returns settings.json reduced to the keys known to be safe. Unreadable
    /// input yields an empty object rather than the original text.
    /// </summary>
    public static string RedactSettings(string? settingsJson)
    {
        var safe = new JObject();
        if (string.IsNullOrWhiteSpace(settingsJson)) return safe.ToString(Formatting.Indented);

        try
        {
            var root = JObject.Parse(settingsJson!);
            foreach (var prop in root.Properties())
            {
                if (SafeSettingsKeys.Contains(prop.Name))
                    safe[prop.Name] = prop.Value.DeepClone();
            }
        }
        catch
        {
            return new JObject().ToString(Formatting.Indented);
        }

        return safe.ToString(Formatting.Indented);
    }
}
