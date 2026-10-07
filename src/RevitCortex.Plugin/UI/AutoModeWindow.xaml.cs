using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Non-modal floating status pill shown while Auto mode or Autopilot is active.
/// Appears bottom-center above Revit (draggable).
///
/// The pill is a pure view: it draws whatever <see cref="AutopilotPillState"/>
/// it is given, either pushed through <see cref="Render"/> or pulled once a
/// second from <see cref="StateProvider"/>. It never reads the session itself.
///
/// Closing it (X / Alt-F4) is equivalent to clicking Stop. Deactivation is
/// surfaced via <see cref="StopRequested"/>, which the host (RevitCortexApp)
/// wires to turn Auto mode off.
/// </summary>
public partial class AutoModeWindow : Window
{
    /// <summary>
    /// Raised once when Auto mode should stop: the user clicked Stop or closed
    /// the window. NOT raised when the host closes the window programmatically
    /// via <see cref="CloseFromHost"/>.
    /// </summary>
    public event Action? StopRequested;

    /// <summary>Raised when the user clicks "Start" on the server-off warning.</summary>
    public event Action? StartServerRequested;

    /// <summary>
    /// Supplies the current state. When set, the pill refreshes itself every
    /// second while it is open (elapsed times, save state, warnings).
    /// </summary>
    public Func<AutopilotPillState>? StateProvider { get; set; }

    // The AI counts as "working" (mark spins) for this long after its last tool call.
    private static readonly TimeSpan WorkingWindow = TimeSpan.FromSeconds(12);

    // Guards against double-firing: the Stop button click closes the window,
    // which would otherwise also fire StopRequested via the Closing handler.
    private bool _stopHandled;

    // Set when the host closes the window because Auto mode was turned off
    // elsewhere (Stop command, document close). Suppresses StopRequested so we
    // don't loop back into deactivation.
    private bool _closingFromHost;
    private readonly IntPtr _ownerHandle;
    private bool _ownerAttached;

    private bool _autopilot;
    private bool _expanded;
    private bool _spinning;
    private string? _pushedStatus;
    private DispatcherTimer? _refreshTimer;

    // The pill stays where the user put it: bottom edge and horizontal center
    // are kept fixed while its size changes (status text, panel open/close).
    private bool _anchored;
    private double _anchorCenterX;
    private double _anchorBottom;

    public AutoModeWindow()
        : this(Process.GetCurrentProcess().MainWindowHandle)
    {
    }

    public AutoModeWindow(IntPtr ownerHandle)
    {
        _ownerHandle = ownerHandle;
        InitializeComponent();
        Title = Localization.T("win.title");
        StopText.Text = Localization.T("win.stop");
        StopButton.ToolTip = Localization.T("win.stop_desc");
        PanelStopButton.Content = Localization.T("win.stop");
        StartServerButton.Content = Localization.T("pill.start_server");
        OpenLogButton.Content = Localization.T("panel.open_log");
        ExpandButton.ToolTip = Localization.T("pill.expand_tip");
        ScriptsChip.ToolTip = Localization.T("pill.scripts_tip");
        PanelScriptsChip.ToolTip = Localization.T("pill.scripts_tip");
        ApprovedBox.ToolTip = Localization.T("pill.approved_tip");
        DeclinedBox.ToolTip = Localization.T("pill.declined_tip");
        StatApprovedLabel.Text = Localization.T("panel.stat_approved");
        StatDeclinedLabel.Text = Localization.T("panel.stat_declined");
        StatDialogsLabel.Text = Localization.T("panel.stat_dialogs");
        StatSavedLabel.Text = Localization.T("panel.stat_saved");
        EventsHeading.Text = Localization.T("panel.events");
        StatusText.Text = Localization.T("win.status_idle");
        SetMode(false);
        AttachOwner();
        Closed += (_, _) => StopRefreshTimer();
    }

    /// <summary>
    /// Switches between plain Auto mode (user present, clicked "Auto") and
    /// Autopilot (user away): heading, explanation and which parts show.
    /// </summary>
    public void SetMode(bool autopilot)
    {
        _autopilot = autopilot;
        var heading = Localization.T(autopilot ? "win.autopilot_heading" : "win.auto_heading");
        HeadingText.Text = heading;
        PanelHeading.Text = heading;
        // The explanation lives in the tooltip; the pill itself stays one line.
        Pill.ToolTip = Localization.T(autopilot ? "win.autopilot_detail" : "win.auto_detail");
        ExpandButton.Visibility = autopilot ? Visibility.Visible : Visibility.Collapsed;
        if (!autopilot) SetExpanded(false);
    }

    /// <summary>
    /// Shows a status line pushed by the host. Used in plain Auto mode and
    /// until the first Autopilot event arrives; after that the pill shows the
    /// latest event from the state it is given.
    /// </summary>
    public void SetStatus(string text)
    {
        _pushedStatus = text;
        StatusText.Text = text;
    }

    /// <summary>
    /// Called (on the UI thread) each time an operation is auto-approved while
    /// Auto mode is on. Auto mode intentionally has no inactivity timeout: it
    /// remains active until the user stops it or the host closes it.
    /// </summary>
    public void RegisterActivity()
    {
        RefreshFromProvider();
    }

    /// <summary>Opens or closes the activity panel above the pill.</summary>
    public void SetExpanded(bool expanded)
    {
        _expanded = expanded && _autopilot;
        ActivityPanel.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandIcon.Data = (Geometry)FindResource(_expanded ? "Vx.Icon.ChevronDown" : "Vx.Icon.ChevronUp");
        ExpandButton.Style = (Style)FindResource(_expanded ? "Vx.Button.PillPrimaryIcon" : "Vx.Button.PillIcon");
        ExpandButton.ToolTip = Localization.T(_expanded ? "pill.collapse_tip" : "pill.expand_tip");
    }

    /// <summary>Draws the given state. Safe to call with any combination of flags.</summary>
    public void Render(AutopilotPillState state)
    {
        if (state == null) return;
        var activity = state.Activity ?? AutopilotSnapshot.Empty;

        if (state.Autopilot != _autopilot) SetMode(state.Autopilot);

        // ── Warnings win over everything else: they explain why nothing happens.
        string? warning = null;
        if (state.Autopilot && state.ServerOff) warning = Localization.T("pill.warn_server");
        else if (state.Autopilot && state.ApprovalsPaused) warning = Localization.T("pill.warn_paused");
        bool warn = warning != null;

        Mark.Visibility = warn ? Visibility.Collapsed : Visibility.Visible;
        WarnIcon.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
        Pill.BorderBrush = (Brush)FindResource(warn ? "Vx.CautionStroke" : "Vx.Stroke");
        StartServerButton.Visibility = state.Autopilot && state.ServerOff
            ? Visibility.Visible : Visibility.Collapsed;

        bool chip = state.Autopilot && state.ScriptsAllowed;
        ScriptsChip.Visibility = chip ? Visibility.Visible : Visibility.Collapsed;
        PanelScriptsChip.Visibility = chip ? Visibility.Visible : Visibility.Collapsed;

        // ── Counters
        bool counters = state.Autopilot && !warn;
        ApprovedText.Text = activity.Approved.ToString();
        DeclinedText.Text = activity.Declined.ToString();
        ApprovedBox.Visibility = counters && activity.Approved > 0 ? Visibility.Visible : Visibility.Collapsed;
        DeclinedBox.Visibility = counters && activity.Declined > 0 ? Visibility.Visible : Visibility.Collapsed;

        // ── Status line
        if (warn)
        {
            StatusText.Text = warning;
            StatusText.Foreground = (Brush)FindResource("Vx.Caution");
        }
        else
        {
            StatusText.Foreground = (Brush)FindResource("Vx.Muted");
            if (state.Autopilot && activity.RecentEvents.Count > 0)
            {
                var latest = activity.RecentEvents[0];
                StatusText.Text = latest.Time.ToString("HH:mm") + "  " + latest.Text;
            }
            else
            {
                StatusText.Text = _pushedStatus ?? Localization.T("win.status_idle");
            }
        }

        // ── Save state
        bool showSave = state.Autopilot && !warn && (state.SavePending || activity.LastSavedAt.HasValue);
        SaveBox.Visibility = showSave ? Visibility.Visible : Visibility.Collapsed;
        if (showSave)
        {
            if (state.SavePending)
            {
                SaveIcon.Visibility = Visibility.Collapsed;
                UnsavedDot.Visibility = Visibility.Visible;
                SaveText.Text = Localization.T("pill.unsaved");
                SaveText.Foreground = (Brush)FindResource("Vx.Caution");
            }
            else
            {
                SaveIcon.Visibility = Visibility.Visible;
                UnsavedDot.Visibility = Visibility.Collapsed;
                SaveText.Text = Localization.T("pill.saved", activity.LastSavedAt!.Value.ToString("HH:mm"));
                SaveText.Foreground = (Brush)FindResource("Vx.InkSoft");
            }
        }

        // ── The mark spins while the AI is calling tools.
        bool working = state.Autopilot && !warn && activity.LastToolCallAt.HasValue
            && state.Now - activity.LastToolCallAt.Value < WorkingWindow;
        SetSpinning(working);

        if (_expanded) RenderPanel(state, activity);
    }

    private void RenderPanel(AutopilotPillState state, AutopilotSnapshot activity)
    {
        UptimeText.Text = activity.StartedAt.HasValue
            ? Localization.T("panel.uptime", FormatDuration(state.Now - activity.StartedAt.Value))
            : string.Empty;

        StatApproved.Text = activity.Approved.ToString();
        StatDeclined.Text = activity.Declined.ToString();
        StatDeclined.Foreground = (Brush)FindResource(activity.Declined > 0 ? "Vx.Caution" : "Vx.Ink");
        StatDialogs.Text = activity.DialogsClosed.ToString();
        StatSaved.Text = activity.LastSavedAt.HasValue ? activity.LastSavedAt.Value.ToString("HH:mm") : "–";

        LastActivityText.Text = activity.LastActivityAt.HasValue
            ? Localization.T("panel.last_activity", FormatDuration(state.Now - activity.LastActivityAt.Value))
            : Localization.T("panel.no_activity");

        EventsPanel.Children.Clear();
        if (activity.RecentEvents.Count == 0)
        {
            EventsPanel.Children.Add(new TextBlock
            {
                Text = Localization.T("panel.no_events"),
                Foreground = (Brush)FindResource("Vx.Muted"),
                Margin = new Thickness(0, 6, 0, 6),
            });
            return;
        }

        foreach (var e in activity.RecentEvents)
            EventsPanel.Children.Add(BuildEventRow(e));
    }

    private UIElement BuildEventRow(AutopilotEvent e)
    {
        string iconKey;
        string brushKey;
        string textBrushKey;
        switch (e.Kind)
        {
            case AutopilotEventKind.Approved:
                iconKey = "Vx.Icon.Check"; brushKey = "Vx.Accent"; textBrushKey = "Vx.Ink"; break;
            case AutopilotEventKind.Declined:
                iconKey = "Vx.Icon.Cross"; brushKey = "Vx.Caution"; textBrushKey = "Vx.Caution"; break;
            case AutopilotEventKind.DialogClosed:
                iconKey = "Vx.Icon.Dialog"; brushKey = "Vx.Muted"; textBrushKey = "Vx.InkSoft"; break;
            default:
                iconKey = "Vx.Icon.Disk"; brushKey = "Vx.Muted"; textBrushKey = "Vx.InkSoft"; break;
        }

        var row = new Grid { Height = 28 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var time = new TextBlock
        {
            Text = e.Time.ToString("HH:mm"),
            FontFamily = (FontFamily)FindResource("Vx.Mono"),
            FontSize = 11.5,
            Foreground = (Brush)FindResource("Vx.Muted"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(time, 0);

        // Check and Cross are drawn in a 12 px box, Dialog and Disk in 14 px.
        double box = e.Kind == AutopilotEventKind.Approved || e.Kind == AutopilotEventKind.Declined ? 12 : 14;
        var icon = new Path
        {
            Width = box,
            Height = box,
            Data = (Geometry)FindResource(iconKey),
            Stroke = (Brush)FindResource(brushKey),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 1);

        var text = new TextBlock
        {
            Text = e.Text,
            FontSize = 12.5,
            Foreground = (Brush)FindResource(textBrushKey),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = e.Text,
        };
        Grid.SetColumn(text, 2);

        row.Children.Add(time);
        row.Children.Add(icon);
        row.Children.Add(text);
        return row;
    }

    /// <summary>"less than 1 min", "47 min", "1 h 12 min" in the UI language.</summary>
    internal static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return Localization.T("time.less_minute");
        if (span.TotalHours < 1) return Localization.T("time.minutes", (int)span.TotalMinutes);
        return Localization.T("time.hours", (int)span.TotalHours, span.Minutes);
    }

    private void SetSpinning(bool on)
    {
        // Respect the Windows "show animations" setting.
        if (on && !SystemParameters.ClientAreaAnimation) on = false;
        if (on == _spinning) return;
        _spinning = on;

        if (on)
        {
            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1.6)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            MarkRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
        else
        {
            MarkRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            MarkRotate.Angle = 0;
        }
    }

    // ── Refresh timer ───────────────────────────────────────────────────────

    private void StartRefreshTimer()
    {
        if (_refreshTimer != null) return;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => RefreshFromProvider();
        _refreshTimer.Start();
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    private void RefreshFromProvider()
    {
        var provider = StateProvider;
        if (provider == null) return;
        try
        {
            Render(provider());
        }
        catch (Exception ex)
        {
            // A status display must never take Revit down.
            Trace.WriteLine($"[RevitCortex] Autopilot pill refresh failed: {ex.Message}");
        }
    }

    // ── Placement ───────────────────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Parent to Revit so the window stays above Revit, but not above other
        // foreground applications. This is intentionally not a global Topmost window.
        AttachOwner();

        // Bottom-center of the work area, above Revit's view control and status
        // bars, so it never covers the ribbon. The user can drag it elsewhere.
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 44;
        CaptureAnchor();

        RefreshFromProvider();
        StartRefreshTimer();
    }

    private void CaptureAnchor()
    {
        _anchorCenterX = Left + ActualWidth / 2;
        _anchorBottom = Top + ActualHeight;
        _anchored = true;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (!_anchored) return;
        // Grow upward and around the center, so the pill itself does not move
        // when its text changes or the panel opens.
        Left = _anchorCenterX - ActualWidth / 2;
        Top = _anchorBottom - ActualHeight;
    }

    private void Pill_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
            CaptureAnchor();
        }
        catch (InvalidOperationException) { /* mouse released mid-drag */ }
    }

    private void AttachOwner()
    {
        if (_ownerAttached || _ownerHandle == IntPtr.Zero) return;

        try
        {
            new WindowInteropHelper(this).Owner = _ownerHandle;
            _ownerAttached = true;
        }
        catch { /* non-critical */ }
    }

    // ── Actions ─────────────────────────────────────────────────────────────

    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        SetExpanded(!_expanded);
        RefreshFromProvider();
    }

    private void StartServer_Click(object sender, RoutedEventArgs e)
    {
        StartServerRequested?.Invoke();
        RefreshFromProvider();
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        OpenLogFile();
    }

    /// <summary>Opens autopilot.log with the default app. Never throws.</summary>
    internal static void OpenLogFile()
    {
        try
        {
            var path = UnattendedLog.FilePath;
            if (!System.IO.File.Exists(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[RevitCortex] Could not open autopilot.log: {ex.Message}");
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        RequestStop();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // X / Alt-F4 path: treat as Stop, unless the host is closing us.
        if (!_closingFromHost)
            RequestStop();
        base.OnClosing(e);
    }

    private void RequestStop()
    {
        if (_stopHandled) return;
        _stopHandled = true;
        StopRequested?.Invoke();
    }

    /// <summary>
    /// Closes the window without raising <see cref="StopRequested"/>. Called by
    /// the host when Auto mode is turned off from somewhere other than this
    /// window (e.g. a future Stop command, or document close/reinitialize).
    /// </summary>
    public void CloseFromHost()
    {
        _closingFromHost = true;
        Close();
    }
}
