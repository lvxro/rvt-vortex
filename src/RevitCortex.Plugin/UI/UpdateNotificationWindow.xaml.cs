using RevitCortex.Plugin.Updates;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Non-modal floating notification shown at Revit startup when a newer
/// RevitCortex version is available. Owns the full download → install flow
/// so the user never has to open Settings.
///
/// States: Idle → Downloading → ConfirmInstall → Installing/Error
/// </summary>
public partial class UpdateNotificationWindow : Window
{
    private enum NotifState { Idle, Downloading, ConfirmInstall, Installing, Error }

    private NotifState _state = NotifState.Idle;
    private DispatcherTimer? _pollTimer;

    public UpdateNotificationWindow()
    {
        InitializeComponent();
        Title = Localization.T("upd.window_title");
        ConfirmText.Text = Localization.T("upd.confirm_note");
        NotesHeading.Text = Localization.T("upd.notes_heading");
        CloseButton.ToolTip = Localization.T("upd.later");
        PrimaryButton.Content = Localization.T("upd.update_now");
        SecondaryButton.Content = Localization.T("upd.later");
        Refresh();
    }

    // The window is larger than the card by this much on every side (room for
    // the drop shadow), so placement compensates to keep a 20 px visual gap.
    private const double ShadowInset = 24;
    private const double ScreenGap = 20;

    // ── Startup ──────────────────────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Parent this window to the Revit main window so it stays in front
        // of Revit but not above other applications.
        try
        {
            var handle = Process.GetCurrentProcess().MainWindowHandle;
            if (handle != IntPtr.Zero)
                new WindowInteropHelper(this).Owner = handle;
        }
        catch { /* non-critical */ }

        // Position bottom-right of the primary screen with a small margin.
        PlaceBottomRight();
    }

    private void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - ScreenGap + ShadowInset;
        Top  = area.Bottom - ActualHeight - ScreenGap + ShadowInset;
    }

    private void Card_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { DragMove(); } catch (InvalidOperationException) { /* mouse released mid-drag */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Same as "Later": a download in progress keeps going and the Settings
        // page still shows the update banner.
        StopPolling();
        Close();
    }

    internal static string ShortVersion(Version v)
    {
        // Assembly versions carry a fourth ".0" that means nothing to the user.
        return v.Build >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var info = UpdateChecker.Latest;
        if (info == null) { Close(); return; }

        VersionFrom.Text = ShortVersion(UpdateChecker.CurrentVersion);
        VersionTo.Text = ShortVersion(info.RemoteVersion);

        // The version jump matters while deciding; after that the title names
        // the version and a second line would only repeat it.
        VersionRow.Visibility = _state == NotifState.Idle ? Visibility.Visible : Visibility.Collapsed;

        // Everything optional starts hidden; each state turns on what it needs.
        NotesBox.Visibility = Visibility.Collapsed;
        NotifDetail.Visibility = Visibility.Collapsed;
        ProgressGrid.Visibility = Visibility.Collapsed;
        ConfirmBorder.Visibility = Visibility.Collapsed;

        switch (_state)
        {
            case NotifState.Idle:
                NotifTitle.Text = Localization.T("upd.available", ShortVersion(info.RemoteVersion));
                if (!string.IsNullOrWhiteSpace(info.Changelog))
                {
                    NotesText.Text = info.Changelog.Trim();
                    NotesBox.Visibility = Visibility.Visible;
                }
                SetPrimary(Localization.T("upd.update_now"), accent: true);
                SetSecondary(Localization.T("upd.later"), visible: true);
                break;

            case NotifState.Downloading:
                var (recv, total) = UpdateChecker.DownloadProgress;
                string prog = total > 0
                    ? $"{recv / 1_048_576.0:F0} / {total / 1_048_576.0:F0} MB"
                    : Localization.T("upd.downloaded_mb", (recv / 1_048_576.0).ToString("F0"));
                double pct = total > 0 ? recv * 100.0 / total : 0;

                NotifTitle.Text = Localization.T("upd.downloading_title", ShortVersion(info.RemoteVersion));
                ShowDetail(Localization.T("upd.downloading_detail"));
                ProgressGrid.Visibility = Visibility.Visible;
                ProgressBar.Value       = pct;
                ProgressText.Text       = prog;
                ProgressPercent.Text    = total > 0 ? $"{pct:F0} %" : string.Empty;
                SetPrimary(Localization.T("upd.cancel"), accent: false);
                SetSecondary(null, visible: false);
                break;

            case NotifState.ConfirmInstall:
                NotifTitle.Text = Localization.T("upd.ready");
                ShowDetail(Localization.T("upd.downloaded_detail", ShortVersion(info.RemoteVersion)));
                ConfirmBorder.Visibility = Visibility.Visible;
                SetPrimary(Localization.T("upd.install_close"), accent: true);
                SetSecondary(Localization.T("upd.cancel"), visible: true);
                break;

            case NotifState.Installing:
                NotifTitle.Text  = Localization.T("upd.installing");
                ShowDetail(Localization.T("upd.installing_detail"));
                SetPrimary(Localization.T("upd.close_revit"), accent: true);
                SetSecondary(null, visible: false);
                break;

            case NotifState.Error:
                NotifTitle.Text  = Localization.T("upd.failed");
                ShowDetail(UpdateChecker.DownloadError ?? Localization.T("upd.unknown_error"));
                SetPrimary(Localization.T("upd.retry"), accent: true);
                SetSecondary(Localization.T("upd.later"), visible: true);
                break;
        }

        // Re-measure so SizeToContent recalculates height, then stay anchored
        // to the bottom-right corner as the card grows or shrinks.
        UpdateLayout();
        if (IsLoaded) PlaceBottomRight();
    }

    private void ShowDetail(string text)
    {
        NotifDetail.Text = text;
        NotifDetail.Visibility = Visibility.Visible;
    }

    private void SetPrimary(string label, bool accent)
    {
        PrimaryButton.Content = label;
        // One accent-colored action per state; "Cancel" is deliberately quiet.
        PrimaryButton.Style = (Style)FindResource(accent ? "Vx.Button.Primary" : "Vx.Button");
    }

    private void SetSecondary(string? label, bool visible)
    {
        SecondaryButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (label != null) SecondaryButton.Content = label;
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case NotifState.Idle:
            case NotifState.Error:
                UpdateChecker.ResetDownload();
                UpdateChecker.StartDownloadAsync();
                _state = NotifState.Downloading;
                StartPolling();
                Refresh();
                break;

            case NotifState.Downloading:
                UpdateChecker.CancelDownload();
                _state = NotifState.Idle;
                StopPolling();
                Refresh();
                break;

            case NotifState.ConfirmInstall:
                StopPolling();
                // H1: only schedule Revit shutdown if the installer actually launched.
                // On UAC denial / launch failure, stay put so the user keeps their work.
                if (!UpdateChecker.LaunchInstaller())
                {
                    _state = NotifState.Idle;
                    Refresh();
                    break;
                }
                _state = NotifState.Installing;
                Refresh();
                // Give the installer a moment to launch, then close Revit.
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    CloseRevit();
                };
                t.Start();
                break;

            case NotifState.Installing:
                CloseRevit();
                break;
        }
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case NotifState.ConfirmInstall:
                // Go back to idle — user changed mind
                _state = NotifState.Idle;
                Refresh();
                break;

            default:
                // "Più tardi" — dismiss; Settings page still shows the banner.
                StopPolling();
                Close();
                break;
        }
    }

    // ── Closing Revit ────────────────────────────────────────────────────────

    private const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Closes the Revit process gracefully. Revit is a native Win32 application, so
    /// Application.Current.Shutdown() does not terminate it (Application.Current is
    /// typically null in the add-in). We post WM_CLOSE to the Revit main window, which
    /// triggers Revit's normal shutdown — including unsaved-changes prompts. The elevated
    /// installer waits for Revit to exit, so this lets the update finish unattended.
    /// </summary>
    private static void CloseRevit()
    {
        try
        {
            var handle = Process.GetCurrentProcess().MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return;
            }
        }
        catch { /* fall back to WPF shutdown below */ }

        try { Application.Current?.Shutdown(); } catch { }
    }

    // ── Download polling ─────────────────────────────────────────────────────

    private void StartPolling()
    {
        if (_pollTimer?.IsEnabled == true) return;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        switch (UpdateChecker.State)
        {
            case UpdateChecker.DownloadState.Downloading:
                Refresh();
                break;

            case UpdateChecker.DownloadState.Ready:
                StopPolling();
                _state = NotifState.ConfirmInstall;
                Refresh();
                break;

            case UpdateChecker.DownloadState.Error:
                StopPolling();
                _state = NotifState.Error;
                Refresh();
                break;
        }
    }
}
