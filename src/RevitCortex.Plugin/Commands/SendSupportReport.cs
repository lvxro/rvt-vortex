using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Security;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Commands;

/// <summary>
/// "Report a bug": collects diagnostics into a ZIP, shows it in Explorer and
/// opens a pre-filled issue on the fork's GitHub repository.
///
/// Issues are public, so the ZIP is REDACTED by default (see
/// <see cref="SupportReportRedactor"/>): no user or machine name, no model
/// name or path, no tool inputs or script source, no Revit journal. Setting
/// "SupportReportIncludePrivateData": true in settings.json builds the full
/// package instead; in that mode no public issue is opened.
///
/// The Outlook code further down is the original RevitCortex e-mail flow,
/// kept for reference and no longer called.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class SendSupportReport : IExternalCommand
{
    private const string SupportEmail = "luigi.dattilo@gpapartners.com";
    private const int DefaultKeepCount = 10;

    // _running  = UI-thread reentrancy guard (this Execute() in flight)
    // _workerBusy = set while the background Outlook STA worker is alive;
    // stays 1 even after Execute() returns when Outlook timed out, so the
    // next click sees "already running" until the zombie worker actually
    // finishes instead of launching a second Outlook draft on top.
    private static int _running;
    private static int _workerBusy;

    /// <summary>Folder where bug-report ZIPs are written and rotated.</summary>
    public static string ReportsFolder => CortexEnvironment.Current.SupportReportsFolder;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var title = Localization.T("support.title");

        // Reject if either: (a) Execute is already on the UI thread stack, or
        // (b) a previous Outlook worker is still alive after a timeout.
        if (System.Threading.Volatile.Read(ref _workerBusy) == 1
            || System.Threading.Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            TaskDialog.Show(title, Localization.T("support.already_running"));
            return Result.Succeeded;
        }

        try
        {
            Directory.CreateDirectory(ReportsFolder);
            RotateOldReports(ReportsFolder, ReadKeepCount());

            bool includePrivate = ReadIncludePrivateData();
            var (zipPath, included, skipped) = BuildReportZip(commandData, includePrivate);

            // RVT Vortex: report on the fork's GitHub issues instead of e-mailing
            // the original author. Issues are PUBLIC, so the pre-filled text
            // carries only versions and the ZIP is redacted by default.
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
            }
            catch { /* non critical */ }

            if (includePrivate)
            {
                // The user opted into the full package (journal, paths, tool
                // inputs). Never steer that toward a public issue.
                TaskDialog.Show(title, Localization.T("support.private_created", zipPath));
                return Result.Succeeded;
            }

            var issueUrl = BuildIssueUrl(commandData);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(issueUrl) { UseShellExecute = true });
            }
            catch { /* the dialog below still shows the link */ }

            TaskDialog.Show(title, Localization.T("support.issue_opened", zipPath, ForkInfo.NewIssueUrl));
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = Localization.T("support.package_failed", ex.Message);
            TaskDialog.Show(title, message);
            return Result.Failed;
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _running, 0);
        }
    }

    // ── Settings + rotation ────────────────────────────────────────────────

    private static int ReadKeepCount()
    {
        try
        {
            string settingsPath = CortexEnvironment.Current.SettingsFilePath;
            if (!File.Exists(settingsPath)) return DefaultKeepCount;

            var json = File.ReadAllText(settingsPath);
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
            var n = obj["SupportReportKeepCount"]?.ToObject<int?>();
            if (n is int v && v >= 1 && v <= 200) return v;
        }
        catch { /* fall through */ }
        return DefaultKeepCount;
    }

    /// <summary>
    /// False unless settings.json opts into the full, unredacted package.
    /// Any read problem means "redact".
    /// </summary>
    private static bool ReadIncludePrivateData()
    {
        try
        {
            string settingsPath = CortexEnvironment.Current.SettingsFilePath;
            if (!File.Exists(settingsPath)) return false;
            return SupportReportRedactor.IncludesPrivateData(File.ReadAllText(settingsPath));
        }
        catch { /* fall through */ }
        return false;
    }

    private static void RotateOldReports(string folder, int keep)
    {
        try
        {
            var zips = new DirectoryInfo(folder)
                .EnumerateFiles("RevitCortex-BugReport-*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            for (int i = keep; i < zips.Count; i++)
            {
                try { zips[i].Delete(); } catch { /* ignore locked files */ }
            }
        }
        catch { /* non fatal */ }
    }

    /// <summary>
    /// Deletes all *.zip reports in <see cref="ReportsFolder"/>. Returns
    /// (deleted, failed). Called from the settings page's "Delete all now"
    /// button after user confirmation.
    /// </summary>
    public static (int deleted, int failed, long bytesFreed) DeleteAllReports()
    {
        int deleted = 0, failed = 0;
        long bytes = 0;
        try
        {
            if (!Directory.Exists(ReportsFolder)) return (0, 0, 0);
            foreach (var f in new DirectoryInfo(ReportsFolder)
                .EnumerateFiles("RevitCortex-BugReport-*.zip", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    long len = f.Length;
                    f.Delete();
                    deleted++;
                    bytes += len;
                }
                catch { failed++; }
            }
        }
        catch { /* ignore enumerate errors */ }
        return (deleted, failed, bytes);
    }

    public static int CountReports()
    {
        try
        {
            if (!Directory.Exists(ReportsFolder)) return 0;
            return new DirectoryInfo(ReportsFolder)
                .EnumerateFiles("RevitCortex-BugReport-*.zip", SearchOption.TopDirectoryOnly)
                .Count();
        }
        catch { return 0; }
    }

    public static long TotalReportsBytes()
    {
        try
        {
            if (!Directory.Exists(ReportsFolder)) return 0;
            return new DirectoryInfo(ReportsFolder)
                .EnumerateFiles("RevitCortex-BugReport-*.zip", SearchOption.TopDirectoryOnly)
                .Sum(f => f.Length);
        }
        catch { return 0; }
    }

    // ── Zip building ────────────────────────────────────────────────────────

    private static (string zipPath, List<string> included, List<string> skipped) BuildReportZip(
        ExternalCommandData commandData, bool includePrivate)
    {
        string rcFolder = CortexEnvironment.Current.RootFolder;
        string reportsDir = ReportsFolder;
        Directory.CreateDirectory(reportsDir);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        // No user name in the file name: the ZIP is meant for a public issue.
        string zipPath = Path.Combine(reportsDir, $"RevitCortex-BugReport-{stamp}.zip");

        var included = new List<string>();
        var skipped = new List<string>();

        // Candidate files (path, entryNameInZip, isOptional).
        // Token usage moved from JSONL to SQLite (usage-mcp.db) around 2026-04.
        // The legacy JSONL is kept here in case pre-migration files still exist
        // in the field for a while.
        var candidates = new List<(string src, string entry, bool optional)>
        {
            (Path.Combine(rcFolder, "audit.jsonl"),                      "audit.jsonl",                 false),
            (Path.Combine(rcFolder, "usage-mcp.db"),                     "usage-mcp.db",                true),
            (Path.Combine(rcFolder, "logs", "token-usage.jsonl"),        "logs/token-usage.jsonl",      true),
            (Path.Combine(rcFolder, "settings.json"),                    "settings.json",               true),
        };

        using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            if (!includePrivate)
            {
                // Default: redacted copies of the audit log and settings, and
                // nothing else from disk (usage data and the Revit journal
                // carry user names, paths and model names).
                AddRedactedAudit(zip, Path.Combine(rcFolder, "audit.jsonl"), included, skipped);
                AddRedactedSettings(zip, Path.Combine(rcFolder, "settings.json"), included, skipped);
                skipped.Add("usage data, Revit journal (private data excluded)");
            }

            foreach (var (src, entry, optional) in candidates)
            {
                if (!includePrivate) break;
                if (!File.Exists(src))
                {
                    skipped.Add($"{entry} (non trovato)");
                    continue;
                }
                try
                {
                    AddFileSafely(zip, src, entry);
                    included.Add(entry);
                }
                catch (Exception ex)
                {
                    skipped.Add($"{entry} ({ex.Message})");
                    if (!optional) throw;
                }
            }

            // Most recent Revit journal (can be large; we cap at 10 MB)
            var journal = includePrivate ? FindLatestJournal() : null;
            if (journal != null)
            {
                try
                {
                    var info = new FileInfo(journal);
                    if (info.Length <= 10 * 1024 * 1024)
                    {
                        AddFileSafely(zip, journal, $"journal/{info.Name}");
                        included.Add($"journal/{info.Name} ({info.Length / 1024} KB)");
                    }
                    else
                    {
                        skipped.Add($"journal/{info.Name} (troppo grande: {info.Length / 1024 / 1024} MB)");
                    }
                }
                catch (Exception ex)
                {
                    skipped.Add($"journal ({ex.Message})");
                }
            }

            // Context file
            var contextEntry = zip.CreateEntry("context.txt");
            using var writer = new StreamWriter(contextEntry.Open(), Encoding.UTF8);
            WriteContextFile(writer, commandData, includePrivate);
            included.Add("context.txt");
        }

        return (zipPath, included, skipped);
    }

    private static void AddFileSafely(ZipArchive zip, string source, string entryName)
    {
        // Read+copy instead of CreateEntryFromFile so we don't hold an exclusive
        // handle on the file (audit.jsonl may be written to concurrently).
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var dst = entry.Open();
        src.CopyTo(dst);
    }

    /// <summary>
    /// Adds audit.jsonl with every entry reduced to tool name, outcome, timing
    /// and a scrubbed error message (last SupportReportRedactor.MaxAuditLines
    /// entries). Like the unredacted path, a read failure aborts the report:
    /// the audit log is the one file a report is useless without.
    /// </summary>
    private static void AddRedactedAudit(ZipArchive zip, string source,
        List<string> included, List<string> skipped)
    {
        if (!File.Exists(source))
        {
            skipped.Add("audit.jsonl (not found)");
            return;
        }

        var entry = zip.CreateEntry("audit.jsonl", CompressionLevel.Optimal);
        int lines;
        // FileShare.ReadWrite: the audit logger may be appending concurrently.
        using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(src, Encoding.UTF8))
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
        {
            lines = SupportReportRedactor.RedactAudit(reader, writer);
        }
        included.Add($"audit.jsonl (redacted, {lines} entries)");
    }

    /// <summary>Adds settings.json reduced to the allowlisted keys. Optional.</summary>
    private static void AddRedactedSettings(ZipArchive zip, string source,
        List<string> included, List<string> skipped)
    {
        if (!File.Exists(source))
        {
            skipped.Add("settings.json (not found)");
            return;
        }

        string redacted;
        try
        {
            redacted = SupportReportRedactor.RedactSettings(File.ReadAllText(source));
        }
        catch (Exception ex)
        {
            skipped.Add($"settings.json ({ex.GetType().Name})");
            return;
        }

        var entry = zip.CreateEntry("settings.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(redacted);
        }
        included.Add("settings.json (redacted)");
    }

    private static string? FindLatestJournal()
    {
        try
        {
            var app = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // Typical path: %LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <year>\Journals
            // We search all Revit versions and take the newest.
            var revitRoot = Path.Combine(app, "Autodesk");
            if (!Directory.Exists(revitRoot)) return null;

            var journals = Directory
                .EnumerateDirectories(revitRoot, "Autodesk Revit*", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    var j = Path.Combine(d, "Journals");
                    return Directory.Exists(j)
                        ? Directory.EnumerateFiles(j, "journal.*.txt")
                        : Enumerable.Empty<string>();
                })
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .FirstOrDefault();

            return journals?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteContextFile(StreamWriter w, ExternalCommandData commandData,
        bool includePrivate)
    {
        w.WriteLine("RVT Vortex diagnostic context");
        w.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local) / {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        if (includePrivate)
        {
            w.WriteLine("Package:   FULL (private data included) - do not post publicly");
            w.WriteLine($"User:      {Environment.UserName}");
            w.WriteLine($"Machine:   {Environment.MachineName}");
        }
        else
        {
            w.WriteLine("Package:   redacted (no user, machine, model name, paths, tool inputs or Revit journal)");
        }
        w.WriteLine($"OS:        {Environment.OSVersion}");
        w.WriteLine($"Culture:   {System.Globalization.CultureInfo.CurrentUICulture.Name}");

        try
        {
            var app = commandData.Application.Application;
            w.WriteLine($"Revit:     {app.VersionName} build {app.VersionBuild} ({app.VersionNumber})");
            w.WriteLine($"Language:  {app.Language}");
        }
        catch (Exception ex)
        {
            w.WriteLine($"Revit:     (unreadable: {ex.Message})");
        }

        try
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc != null)
            {
                if (includePrivate)
                {
                    w.WriteLine($"Document:  {doc.Title}");
                    w.WriteLine($"Path:      {doc.PathName}");
                }
                else
                {
                    w.WriteLine("Document:  (open)");
                }
                w.WriteLine($"Workshared:{doc.IsWorkshared}");
            }
            else
            {
                w.WriteLine("Document:  (none open)");
            }
        }
        catch (Exception ex)
        {
            w.WriteLine($"Document:  (unreadable: {ex.Message})");
        }

        w.WriteLine();
        var pluginVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        // Machine-readable key (used by rclog to match known-issues with reporter_version_max).
        w.WriteLine($"plugin_version: {pluginVersion}");
        // Install and config locations sit under the user's profile, so they
        // name the Windows account: full package only.
        if (includePrivate)
            w.WriteLine("Plugin assembly: " + System.Reflection.Assembly.GetExecutingAssembly().Location);

        // MCP / configuration (Phase 0 trust-cleanup acceptance criterion: the
        // support report must identify the active MCP server, plugin version,
        // Revit version, and the configuration path — so support can triage a
        // single report and know exactly which stack/profile is active).
        w.WriteLine();
        w.WriteLine("Supported MCP server: revitcortex (C# stdio server, RevitCortex.Server.exe)");
        try
        {
            var env = CortexEnvironment.Current;
            w.WriteLine($"Profile:        {env.ProfileName}{(env.IsDev ? " (DEV build)" : "")}");
            if (includePrivate)
            {
                w.WriteLine($"Config folder:  {env.RootFolder}");
                w.WriteLine($"Settings file:  {env.SettingsFilePath}");
            }
            w.WriteLine($"Bridge port:    {env.DefaultPort}");
        }
        catch (Exception ex)
        {
            w.WriteLine($"Profile:        (unreadable: {ex.Message})");
        }
    }

    // ── GitHub issue (RVT Vortex) ───────────────────────────────────────────

    private static string BuildIssueUrl(ExternalCommandData commandData)
    {
        string revit = "?";
        try
        {
            var app = commandData.Application.Application;
            revit = $"{app.VersionName} ({app.VersionNumber})";
        }
        catch { /* keep the issue usable */ }

        var plugin = typeof(SendSupportReport).Assembly.GetName().Version?.ToString() ?? "?";
        var body =
            "**What happened**\n(describe what you were doing and what went wrong)\n\n" +
            "**Environment**\n" +
            $"- {ForkInfo.ProductName}: {plugin}\n" +
            $"- Revit: {revit}\n\n" +
            "**Diagnostic ZIP**\nDrag the ZIP that just opened in Explorer into this box. " +
            "It leaves out your user name, machine name, model names, file paths, tool inputs " +
            "and the Revit journal. Issues are public, so still take a look inside before attaching it.";

        return ForkInfo.NewIssueUrl +
               "?title=" + Uri.EscapeDataString("Bug report: ") +
               "&body=" + Uri.EscapeDataString(body);
    }

    // ── Email body (original RevitCortex flow, kept for reference) ──────────

    private static string BuildEmailBody(ExternalCommandData commandData,
        List<string> included, List<string> skipped)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ciao Luigi,");
        sb.AppendLine();
        sb.AppendLine("ti invio un bug report di RevitCortex Premium.");
        sb.AppendLine();
        sb.AppendLine("── Descrizione del problema ─────────────");
        sb.AppendLine("(descrivi qui cosa stavi facendo e cosa è andato storto)");
        sb.AppendLine();
        sb.AppendLine("── Contesto ─────────────────────────────");
        try
        {
            var app = commandData.Application.Application;
            sb.AppendLine($"Utente:  {Environment.UserName}");
            sb.AppendLine($"Revit:   {app.VersionName} ({app.VersionNumber})");
            var doc = commandData.Application.ActiveUIDocument?.Document;
            sb.AppendLine($"Modello: {(doc != null ? doc.Title : "(nessun documento aperto)")}");
            sb.AppendLine($"Data:    {DateTime.Now:yyyy-MM-dd HH:mm}");
        }
        catch { /* keep the body usable */ }
        sb.AppendLine();
        sb.AppendLine("── File allegati ─────────────────────────");
        foreach (var i in included) sb.AppendLine($"  ✓ {i}");
        if (skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("── Non inclusi ───────────────────────────");
            foreach (var s in skipped) sb.AppendLine($"  - {s}");
        }
        sb.AppendLine();
        sb.AppendLine("Grazie,");
        sb.AppendLine(Environment.UserName);
        return sb.ToString();
    }

    // ── Outlook COM automation ──────────────────────────────────────────────

    /// <summary>
    /// Runs the Outlook COM call on a dedicated STA thread and waits up to
    /// <paramref name="timeout"/>. If Outlook is stuck (hidden modal dialog,
    /// profile prompt, slow startup), the Revit UI thread is never blocked.
    /// When the timeout fires the worker keeps running (background STA thread
    /// holds COM references until Outlook eventually settles), and flips
    /// <see cref="_workerBusy"/> back to 0 in its finally. Subsequent clicks
    /// on the ribbon read _workerBusy and refuse to start a second draft.
    /// We never call Thread.Abort: not supported on net8 and unsafe on net48
    /// with COM.
    /// </summary>
    private static bool TryOpenOutlookWithTimeout(string subject, string body,
        string attachmentPath, TimeSpan timeout)
    {
        int resultFlag = 0;
        var done = new ManualResetEventSlim(false);

        System.Threading.Interlocked.Exchange(ref _workerBusy, 1);

        var thread = new Thread(() =>
        {
            try
            {
                bool ok = false;
                try { ok = TryOpenOutlook(subject, body, attachmentPath); }
                catch { ok = false; }
                System.Threading.Volatile.Write(ref resultFlag, ok ? 1 : 0);
            }
            finally
            {
                done.Set();
                System.Threading.Interlocked.Exchange(ref _workerBusy, 0);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!done.Wait(timeout))
            return false;

        return System.Threading.Volatile.Read(ref resultFlag) == 1;
    }

    private static bool TryOpenOutlook(string subject, string body, string attachmentPath)
    {
        try
        {
            // Late-binding via Type.GetTypeFromProgID avoids a hard reference on
            // Microsoft.Office.Interop.Outlook — works even if Outlook is not
            // installed (we just fall through to the fallback path).
            var outlookType = Type.GetTypeFromProgID("Outlook.Application");
            if (outlookType == null) return false;

            dynamic? outlook = Activator.CreateInstance(outlookType);
            if (outlook == null) return false;

            const int olMailItem = 0;
            dynamic mail = outlook.CreateItem(olMailItem);
            mail.Subject = subject;
            mail.Body = body;

            // Add recipient
            dynamic recipient = mail.Recipients.Add(SupportEmail);
            recipient.Resolve();

            // Attach file
            if (File.Exists(attachmentPath))
                mail.Attachments.Add(attachmentPath, 1 /* olByValue */, Type.Missing, Type.Missing);

            mail.Display(false); // non-modal: user can edit and click Send
            Marshal.ReleaseComObject(recipient);
            Marshal.ReleaseComObject(mail);
            Marshal.ReleaseComObject(outlook);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
