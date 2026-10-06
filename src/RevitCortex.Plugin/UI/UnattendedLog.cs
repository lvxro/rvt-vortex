using System;
using System.IO;
using System.Text;
using RevitCortex.Core.Hosting;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Human-readable Autopilot log of everything decided automatically while the
/// user was away: operations approved or declined, Revit dialogs dismissed and
/// auto-saves. Written in the UI language to &lt;RootFolder&gt;/autopilot.log
/// (e.g. %USERPROFILE%\.revitcortex\autopilot.log). Never throws.
/// </summary>
public static class UnattendedLog
{
    private static readonly object Lock = new object();

    public static string FilePath =>
        Path.Combine(CortexEnvironment.Current.RootFolder, "autopilot.log");

    public static void Write(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}";
            lock (Lock)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Unattended log write failed: {ex.Message}");
        }
    }

    public static void Separator(string title)
    {
        Write(new string('-', 20) + " " + title + " " + new string('-', 20));
    }

    /// <summary>Collapses whitespace and truncates long dialog texts for the log.</summary>
    public static string Shorten(string? text, int max = 300)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text!.Length);
        var lastWasSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        var s = sb.ToString().Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
