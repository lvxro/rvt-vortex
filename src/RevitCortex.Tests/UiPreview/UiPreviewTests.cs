using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RevitCortex.Core.Session;
using RevitCortex.Plugin.UI;
using RevitCortex.Plugin.Updates;
using Xunit;

namespace RevitCortex.Tests.UiPreview;

/// <summary>
/// Builds every plugin window without Revit, in the states a user can meet.
///
/// Always: constructs each window and lays it out, so a XAML mistake (a missing
/// resource key, a broken template, a renamed element) fails here instead of
/// when someone opens the window inside Revit.
///
/// With UI_PREVIEW_DIR set (the "UI preview" workflow): also shows each window
/// and saves it as a PNG in that folder, so a change to the interface can be
/// reviewed as pictures. UI_PREVIEW_LANG picks the language (en / es / it).
/// </summary>
public class UiPreviewTests
{
    private static readonly string? OutputDir = Environment.GetEnvironmentVariable("UI_PREVIEW_DIR");
    private static readonly string? Language = Environment.GetEnvironmentVariable("UI_PREVIEW_LANG");
    private static bool Capturing => !string.IsNullOrWhiteSpace(OutputDir);

    // A neutral mid-grey behind the floating (transparent) windows, about what
    // a Revit view looks like behind them, so their shadows are visible.
    private static readonly Color Backdrop = Color.FromRgb(0x6B, 0x72, 0x7C);

    private static readonly DateTime Now = new DateTime(2026, 3, 9, 14, 37, 20);

    [Fact]
    public void Every_window_builds_in_every_state()
    {
        var failures = new List<string>();

        RunOnStaThread(() =>
        {
            if (Capturing)
            {
                Directory.CreateDirectory(OutputDir!);
                if (!string.IsNullOrWhiteSpace(Language)) ForceLanguage(Language!);
            }

            var scenarios = new (string Name, Action Run)[]
            {
                ("ribbon-icons", RibbonIcons),
                ("settings-general", SettingsGeneral),
                ("settings-general-on-update", SettingsGeneralRunningWithUpdate),
                ("settings-tools", SettingsTools),
                ("settings-tools-search", SettingsToolsSearch),
                ("update-states", UpdateStates),
                ("pill-states", PillStates),
                ("pill-panel", PillPanel),
                ("autopilot-summary", AutopilotSummary),
            };

            foreach (var (name, run) in scenarios)
            {
                // One broken screen must not hide the others.
                try { run(); }
                catch (Exception ex) { failures.Add($"{name}: {ex}"); }
            }

            SetLatestUpdate(null);
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    // ── Scenarios ────────────────────────────────────────────────────────

    private static void RibbonIcons()
    {
        // The ribbon itself belongs to Revit; the plugin only supplies these
        // bitmaps. Shown on Revit's light and dark ribbon colors, 32 px (large
        // buttons) and 16 px (stacked buttons), enlarged 3x.
        var large = new[]
        {
            IconFactory.CreateConnectionIcon(32, false),
            IconFactory.CreateConnectionIcon(32, true),
            IconFactory.CreateAutopilotIcon(32, false),
            IconFactory.CreateAutopilotIcon(32, true),
        };
        var small = new[]
        {
            IconFactory.CreateSettingsIcon(16),
            IconFactory.CreatePowerBiIcon(16),
            IconFactory.CreateSupportIcon(16),
            IconFactory.CreateConnectionIcon(16, false),
            IconFactory.CreateConnectionIcon(16, true),
            IconFactory.CreateAutopilotIcon(16, false),
            IconFactory.CreateAutopilotIcon(16, true),
        };
        if (!Capturing) return;

        const double zoom = 3, gap = 14, pad = 18;
        double rowHeight = pad + 32 * zoom + gap + 16 * zoom + pad;
        double width = pad * 2 + small.Length * (32 * zoom + gap);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            var ribbons = new[] { Color.FromRgb(0xF5, 0xF5, 0xF5), Color.FromRgb(0x3B, 0x44, 0x53) };
            for (int r = 0; r < ribbons.Length; r++)
            {
                double top = r * rowHeight;
                dc.DrawRectangle(new SolidColorBrush(ribbons[r]), null, new Rect(0, top, width, rowHeight));
                double x = pad;
                foreach (var icon in large)
                {
                    dc.DrawImage(icon, new Rect(x, top + pad, 32 * zoom, 32 * zoom));
                    x += 32 * zoom + gap;
                }
                x = pad;
                foreach (var icon in small)
                {
                    dc.DrawImage(icon, new Rect(x, top + pad + 32 * zoom + gap, 16 * zoom, 16 * zoom));
                    x += 16 * zoom + gap;
                }
            }
        }
        SavePng(RenderVisual(visual, width, rowHeight * 2, 1), "ribbon-icons");
    }

    private static void SettingsGeneral()
    {
        SetLatestUpdate(null);
        var window = new SettingsWindow();
        Build(window, "settings-general");
    }

    private static void SettingsGeneralRunningWithUpdate()
    {
        SetLatestUpdate(SampleUpdate());
        var window = new SettingsWindow();
        window.GeneralPage.ApplyServerState(running: true, port: 8080);
        Build(window, "settings-general-on-update");
        SetLatestUpdate(null);
    }

    private static void SettingsTools()
    {
        var window = new SettingsWindow();
        window.ToolsPage.SetTools(SampleTools());
        window.ShowToolsTab();
        Build(window, "settings-tools", beforeCapture: () =>
        {
            // Open the first category, as a user would.
            var list = (ItemsControl)window.ToolsPage.FindName("CategoryList");
            list.ItemsSource.Cast<CategoryGroup>().First().ToggleExpanded();
        });
    }

    private static void SettingsToolsSearch()
    {
        var window = new SettingsWindow();
        window.ToolsPage.SetTools(SampleTools());
        window.ShowToolsTab();
        Build(window, "settings-tools-search", beforeCapture: () =>
        {
            ((TextBox)window.ToolsPage.FindName("SearchBox")).Text = "param";
        });
    }

    private static void UpdateStates()
    {
        SetLatestUpdate(SampleUpdate());
        var shots = new List<BitmapSource?>();

        // Idle (an update is available), then the three later states, set the
        // way the window sets them itself.
        foreach (var state in new[] { "Idle", "Downloading", "ConfirmInstall", "Error" })
        {
            var window = new UpdateNotificationWindow();
            if (state == "Downloading") SetDownloadProgress(59_600_000, 96_100_000);
            if (state != "Idle") SetUpdateWindowState(window, state);
            shots.Add(Build(window, null));
        }

        SetDownloadProgress(0, 0);
        SetLatestUpdate(null);
        SaveSheet(shots, "update-states");
    }

    private static void PillStates()
    {
        var saved = Now.AddMinutes(-5);
        var approved = new AutopilotEvent(Now.AddMinutes(-2), AutopilotEventKind.Approved,
            "Aprobado: modificar parámetros (48)");
        var declined = new AutopilotEvent(Now.AddMinutes(-1), AutopilotEventKind.Declined,
            "Rechazado: ejecutar script C# (1)");

        var states = new (bool Autopilot, AutopilotPillState State)[]
        {
            // Plain Auto mode (the user is present and clicked "Auto").
            (false, new AutopilotPillState { Now = Now }),
            // Autopilot, nothing has happened yet.
            (true, new AutopilotPillState
            {
                Autopilot = true, Now = Now,
                Activity = Snapshot(0, 0, null, null),
            }),
            // Working: scripts allowed, model saved.
            (true, new AutopilotPillState
            {
                Autopilot = true, ScriptsAllowed = true, Now = Now,
                Activity = Snapshot(12, 0, saved, Now.AddSeconds(-3), approved),
            }),
            // Changes not saved yet.
            (true, new AutopilotPillState
            {
                Autopilot = true, SavePending = true, Now = Now,
                Activity = Snapshot(13, 0, saved, Now.AddSeconds(-40), approved),
            }),
            // One step was declined.
            (true, new AutopilotPillState
            {
                Autopilot = true, Now = Now,
                Activity = Snapshot(12, 1, saved, Now.AddSeconds(-40), approved, declined),
            }),
            // Warning: another document is active.
            (true, new AutopilotPillState
            {
                Autopilot = true, ApprovalsPaused = true, Now = Now,
                Activity = Snapshot(12, 0, saved, null, approved),
            }),
            // Warning: the server is off.
            (true, new AutopilotPillState
            {
                Autopilot = true, ServerOff = true, Now = Now,
                Activity = Snapshot(12, 0, saved, null, approved),
            }),
        };

        var shots = new List<BitmapSource?>();
        foreach (var (autopilot, state) in states)
        {
            var window = new AutoModeWindow(IntPtr.Zero);
            window.SetMode(autopilot);
            window.Render(state);
            shots.Add(Build(window, null, beforeCapture: () => window.Render(state),
                close: window.CloseFromHost));
        }
        SaveSheet(shots, "pill-states");
    }

    private static void PillPanel()
    {
        var events = new[]
        {
            new AutopilotEvent(Now.AddMinutes(-6), AutopilotEventKind.Approved, "Aprobado: renombrar (12)"),
            new AutopilotEvent(Now.AddMinutes(-5), AutopilotEventKind.Saved, "Modelo guardado"),
            new AutopilotEvent(Now.AddMinutes(-4), AutopilotEventKind.DialogClosed, "Aviso de Revit cerrado con Cancelar"),
            new AutopilotEvent(Now.AddMinutes(-2), AutopilotEventKind.Approved, "Aprobado: modificar parámetros (48)"),
            new AutopilotEvent(Now.AddMinutes(-1), AutopilotEventKind.Declined, "Rechazado: ejecutar script C# (1)"),
        };
        var state = new AutopilotPillState
        {
            Autopilot = true, ScriptsAllowed = true, Now = Now,
            // RecentEvents is newest first.
            Activity = new AutopilotSnapshot(Now.AddMinutes(-47), 12, 1, 1, Now.AddMinutes(-5),
                Now.AddMinutes(-1), Now.AddSeconds(-4), Enumerable.Reverse(events).ToArray(), new[] { events[4] }),
        };

        var window = new AutoModeWindow(IntPtr.Zero);
        window.SetMode(true);
        window.SetExpanded(true);
        window.Render(state);
        Build(window, "pill-panel", beforeCapture: () => window.Render(state), close: window.CloseFromHost);
    }

    private static void AutopilotSummary()
    {
        var declined = new[]
        {
            new AutopilotEvent(Now.AddMinutes(-38), AutopilotEventKind.Declined, "Rechazado: ejecutar script C# (1)"),
            new AutopilotEvent(Now.AddMinutes(-16), AutopilotEventKind.Declined, "Rechazado: eliminar elementos (6)"),
        };
        var withPending = new AutopilotSnapshot(Now.AddMinutes(-72), 31, 2, 3, Now.AddMinutes(-2),
            Now, Now, Array.Empty<AutopilotEvent>(), declined);
        var clean = new AutopilotSnapshot(Now.AddMinutes(-25), 9, 0, 0, Now.AddMinutes(-1),
            Now, Now, Array.Empty<AutopilotEvent>(), Array.Empty<AutopilotEvent>());

        var shots = new List<BitmapSource?>
        {
            Build(new AutopilotSummaryWindow(withPending, Now, IntPtr.Zero), null),
            Build(new AutopilotSummaryWindow(clean, Now, IntPtr.Zero), null),
        };
        SaveSheet(shots, "autopilot-summary");
    }

    // ── Sample data ──────────────────────────────────────────────────────

    private static AutopilotSnapshot Snapshot(int approved, int declined, DateTime? savedAt,
        DateTime? lastToolCall, params AutopilotEvent[] oldestFirst)
    {
        var newestFirst = Enumerable.Reverse(oldestFirst).ToArray();
        var declinedEvents = oldestFirst.Where(e => e.Kind == AutopilotEventKind.Declined).ToArray();
        DateTime? lastActivity = newestFirst.Length > 0 ? newestFirst[0].Time : lastToolCall;
        return new AutopilotSnapshot(Now.AddMinutes(-47), approved, declined, 0, savedAt,
            lastActivity, lastToolCall, newestFirst, declinedEvents);
    }

    private static UpdateInfo SampleUpdate() => new UpdateInfo(
        new Version(1, 2, 0),
        "https://github.com/lvxro/rvt-vortex/releases/download/v1.2.0/RVT-Vortex-v1.2.0.zip",
        "  - New dark interface for Settings, the update notice and the Autopilot pill.\n" +
        "  - The Autopilot pill shows what was approved, declined and saved.\n" +
        "  - A summary appears when Autopilot stops.",
        hasUpdate: true);

    private static List<(string Name, string Category, string Description, bool IsEnabled)> SampleTools() => new()
    {
        ("create_dimensions", "Annotations", "Creates linear dimension annotations between elementIds (2+) or startPoint/endPoint.", true),
        ("create_text_note", "Annotations", "Creates one or more text notes in the active or specified view.", true),
        ("tag_rooms", "Annotations", "Tags all or specified rooms in the current view.", true),
        ("send_code_to_revit", "Code", "Runs a C# script inside Revit. Last resort; off by default.", false),
        ("get_element_parameters", "Elements", "Returns instance and type parameters for the given element ids.", true),
        ("set_element_parameters", "Elements", "Sets one or more parameters on a single element.", true),
        ("bulk_modify_parameter_values", "Elements", "Sets the same parameter value on many elements, with a dry run first.", true),
        ("delete_element", "Elements", "Deletes elements by id after a confirmation.", true),
        ("ifc_list_export_configurations", "IFC", "Lists the IFC export configurations saved in the document.", true),
        ("check_model_health", "Model", "Returns a health score and the main issues of the active model.", true),
        ("get_warnings", "Model", "Lists the warnings of the active model.", true),
        ("get_shared_parameters", "Parameters", "Lists the shared parameters bound in the document.", true),
        ("get_schedule_data", "Schedules", "Returns the rows of a schedule.", true),
        ("create_view", "Views", "Creates a floor plan, section or 3D view.", true),
    };

    // ── Building and capturing ───────────────────────────────────────────

    /// <summary>
    /// Lays the window out and, when capturing, shows it and returns its
    /// picture (saved as <paramref name="saveAs"/>.png when that is given).
    /// </summary>
    private static BitmapSource? Build(Window window, string? saveAs, Action? beforeCapture = null,
        Action? close = null)
    {
        if (!Capturing)
        {
            // No window on screen: lay out the content on its own.
            var content = (FrameworkElement)window.Content;
            double width = double.IsNaN(window.Width) ? 900 : window.Width;
            double height = double.IsNaN(window.Height) ? 700 : window.Height;
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(content.DesiredSize));
            content.UpdateLayout();
            Pump();
            beforeCapture?.Invoke();
            content.UpdateLayout();
            Pump();
            return null;
        }

        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        try
        {
            Pump();
            beforeCapture?.Invoke();
            window.UpdateLayout();
            Pump();

            // The window's first visual child is its client area: no title
            // bar, and for the floating windows it includes the shadow margin.
            var client = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            bool transparent = window.AllowsTransparency;
            var shot = RenderVisual(client, client.ActualWidth, client.ActualHeight, 2,
                transparent ? Backdrop : (Color?)null);
            if (saveAs != null) SavePng(shot, saveAs);
            return shot;
        }
        finally
        {
            if (close != null) close();
            else window.Close();
            Pump();
        }
    }

    private static BitmapSource RenderVisual(Visual visual, double width, double height, double scale,
        Color? backdrop = null)
    {
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));

        var shot = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        shot.Render(visual);
        shot.Freeze();
        if (backdrop == null) return shot;

        var composed = new DrawingVisual();
        using (var dc = composed.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(backdrop.Value), null, new Rect(0, 0, width, height));
            dc.DrawImage(shot, new Rect(0, 0, width, height));
        }
        var result = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        result.Render(composed);
        result.Freeze();
        return result;
    }

    /// <summary>Stacks several shots into one picture, top to bottom.</summary>
    private static void SaveSheet(List<BitmapSource?> shots, string name)
    {
        var images = shots.Where(s => s != null).Cast<BitmapSource>().ToList();
        if (!Capturing || images.Count == 0) return;

        // Shots are rendered at 2x: PixelWidth is twice the layout width.
        double width = images.Max(i => i.Width);
        double height = images.Sum(i => i.Height);

        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Backdrop), null, new Rect(0, 0, width, height));
            double y = 0;
            foreach (var image in images)
            {
                dc.DrawImage(image, new Rect((width - image.Width) / 2, y, image.Width, image.Height));
                y += image.Height;
            }
        }
        SavePng(RenderVisual(sheet, width, height, 2), name);
    }

    private static void SavePng(BitmapSource image, string name)
    {
        if (!Capturing) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(OutputDir!, name + ".png"));
        encoder.Save(stream);
    }

    /// <summary>Lets queued layout, navigation and Loaded handlers run.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    // ── Reaching into the plugin (it has no InternalsVisibleTo) ───────────

    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private static void ForceLanguage(string locale)
    {
        var localization = typeof(SettingsWindow).Assembly.GetType("RevitCortex.Plugin.UI.Localization", true)!;
        localization.GetField("_cachedLocale", Hidden)!.SetValue(null, locale);
    }

    private static void SetLatestUpdate(UpdateInfo? info) =>
        typeof(UpdateChecker).GetProperty(nameof(UpdateChecker.Latest))!.SetValue(null, info);

    private static void SetDownloadProgress(long received, long total) =>
        typeof(UpdateChecker).GetField("_downloadProgress", Hidden)!.SetValue(null, (received, total));

    private static void SetUpdateWindowState(UpdateNotificationWindow window, string stateName)
    {
        var field = typeof(UpdateNotificationWindow).GetField("_state", Hidden)!;
        field.SetValue(window, Enum.Parse(field.FieldType, stateName));
        typeof(UpdateNotificationWindow).GetMethod("Refresh", Hidden)!.Invoke(window, null);
    }
}
