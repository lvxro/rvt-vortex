using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using RevitCortex.Plugin.Commands;
using RevitCortex.Plugin.Updates;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

namespace RevitCortex.Plugin.UI;

public partial class GeneralSettingsPage : Page
{
    private static int DefaultPort => CortexEnvironment.Current.DefaultPort;
    private const string DefaultLogLevel = "Info";
    private const int DefaultKeepCount = 10;
    private DispatcherTimer? _saveFeedbackTimer;
    private int _originalPort;
    private DispatcherTimer? _downloadTimer;
    private bool _serverRunning;

    private static string SettingsFilePath => CortexEnvironment.Current.SettingsFilePath;

    public GeneralSettingsPage()
    {
        InitializeComponent();
        ApplyLocalizedStrings();
        LoadSettings();
        RefreshConnectionStatus();
        RefreshUpdateBanner();
        // The update check runs once at plugin startup on a background thread;
        // when the user opens Settings it may or may not have completed yet.
        // Re-check every second for ~10 s to catch the late reply, then stop.
        StartUpdateBannerPolling();

        // Subscribe to real-time server state changes so the status card
        // updates immediately when the user clicks Vortex Switch. Through
        // PluginHost, so the page can also be built where Revit is absent.
        PluginHost.SubscribeServiceState(OnServiceStateChanged);
        Unloaded += (_, _) => PluginHost.UnsubscribeServiceState(OnServiceStateChanged);
    }

    private void OnServiceStateChanged()
    {
        // The event may fire from the Revit main thread or a background thread.
        if (Dispatcher.CheckAccess()) RefreshConnectionStatus();
        else Dispatcher.BeginInvoke((Action)RefreshConnectionStatus);
    }

    private void ApplyLocalizedStrings()
    {
        SecServerTitle.Text = Localization.T("settings.sec_server");
        SecServerHelp.Text = Localization.T("settings.sec_server_help");
        PortLabel.Text = Localization.T("settings.port");
        PortHint.Text = Localization.T("settings.port_hint");

        SecLogTitle.Text = Localization.T("settings.sec_log");
        SecLogHelp.Text = Localization.T("settings.sec_log_help");

        SecProtectTitle.Text = Localization.T("settings.sec_protect");
        SecProtectHelp.Text = Localization.T("settings.sec_protect_help");
        ReadOnlyTitle.Text = Localization.T("settings.readonly");
        ReadOnlyHelp.Text = Localization.T("settings.readonly_help");

        SupportReportsTitle.Text = Localization.T("settings.sec_reports");
        SupportReportsSubtitle.Text = Localization.T("settings.sec_reports_help");
        KeepLabel.Text = Localization.T("settings.keep_label");
        KeepSuffix.Text = Localization.T("settings.keep_suffix");
        OpenReportsFolderButton.Content = Localization.T("support.settings.open_folder");
        DeleteAllReportsButton.Content = Localization.T("settings.delete_reports");
        EnableTelemetryCheckBox.Content = Localization.T("telemetry.settings_toggle");

        FooterHint.Text = Localization.T("settings.footer_hint");
        ResetButton.Content = Localization.T("settings.reset");
        SaveButton.Content = Localization.T("settings.save");
        UpdateManualButton.Content = Localization.T("upd.manual_download");

        // RVT Vortex: telemetry is off (ForkInfo.TelemetryEnabled), so a toggle
        // that changes nothing would only mislead. Hide the whole row.
        if (!ForkInfo.TelemetryEnabled)
        {
            TelemetryPanel.Visibility = System.Windows.Visibility.Collapsed;
            EnableTelemetryCheckBox.Visibility = System.Windows.Visibility.Collapsed;
            TelemetrySeparator.Visibility = System.Windows.Visibility.Collapsed;
        }
    }

    private void RefreshUpdateBanner()
    {
        var info = UpdateChecker.Latest;
        if (info?.HasUpdate != true)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            StopDownloadTimer();
            return;
        }

        UpdateBanner.Visibility = Visibility.Visible;

        switch (UpdateChecker.State)
        {
            case UpdateChecker.DownloadState.Idle:
                UpdateTitle.Text = Localization.T("upd.available",
                    UpdateNotificationWindow.ShortVersion(info.RemoteVersion));
                UpdateDetail.Text = Localization.T("upd.current_with_notes",
                    UpdateNotificationWindow.ShortVersion(UpdateChecker.CurrentVersion), info.Changelog);
                UpdateProgressGrid.Visibility = Visibility.Collapsed;
                SetActionButton(Localization.T("upd.download_install"), accent: true, isEnabled: true);
                UpdateManualButton.Visibility = Visibility.Collapsed;
                StopDownloadTimer();
                break;

            case UpdateChecker.DownloadState.Downloading:
                var (recv, total) = UpdateChecker.DownloadProgress;
                string progress = total > 0
                    ? $"{recv / 1_048_576.0:F0} / {total / 1_048_576.0:F0} MB"
                    : Localization.T("upd.downloaded_mb", (recv / 1_048_576.0).ToString("F0"));
                double pct = total > 0 ? recv * 100.0 / total : 0;
                UpdateTitle.Text = Localization.T("upd.downloading", progress);
                UpdateDetail.Text = string.Empty;
                UpdateProgress.Value = pct;
                UpdateProgressText.Text = progress;
                UpdateProgressGrid.Visibility = Visibility.Visible;
                SetActionButton(Localization.T("upd.cancel"), accent: false, isEnabled: true);
                UpdateManualButton.Visibility = Visibility.Collapsed;
                StartDownloadTimer();
                break;

            case UpdateChecker.DownloadState.Ready:
                UpdateTitle.Text = Localization.T("upd.ready");
                UpdateDetail.Text = Localization.T("upd.ready_warning");
                UpdateProgressGrid.Visibility = Visibility.Collapsed;
                SetActionButton(Localization.T("upd.install_and_close"), accent: true, isEnabled: true);
                UpdateManualButton.Visibility = Visibility.Collapsed;
                StopDownloadTimer();
                break;

            case UpdateChecker.DownloadState.Installing:
                UpdateTitle.Text = Localization.T("upd.installing_closing");
                UpdateDetail.Text = Localization.T("upd.installing_detail");
                UpdateProgressGrid.Visibility = Visibility.Collapsed;
                SetActionButton(Localization.T("upd.install_and_close"), accent: true, isEnabled: false);
                UpdateManualButton.Visibility = Visibility.Collapsed;
                StopDownloadTimer();
                break;

            case UpdateChecker.DownloadState.Done:
                UpdateBanner.Visibility = Visibility.Collapsed;
                StopDownloadTimer();
                break;

            case UpdateChecker.DownloadState.Error:
                UpdateTitle.Text = Localization.T("upd.failed");
                UpdateDetail.Text = UpdateChecker.DownloadError ?? Localization.T("upd.unknown_error");
                UpdateProgressGrid.Visibility = Visibility.Collapsed;
                SetActionButton(Localization.T("upd.retry"), accent: true, isEnabled: true);
                UpdateManualButton.Visibility = Visibility.Visible;
                StopDownloadTimer();
                break;
        }
    }

    private void SetActionButton(string label, bool accent, bool isEnabled)
    {
        UpdateActionButton.Content = label;
        // One accent-colored action per state; "Cancel" is deliberately quiet.
        UpdateActionButton.Style = (Style)FindResource(accent ? "Vx.Button.Primary" : "Vx.Button");
        UpdateActionButton.IsEnabled = isEnabled;
    }

    private void StartDownloadTimer()
    {
        if (_downloadTimer?.IsEnabled == true) return;
        StopDownloadTimer(); // stop and null the old one before creating a new one
        _downloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _downloadTimer.Tick += (_, _) => RefreshUpdateBanner();
        _downloadTimer.Start();
    }

    private void StopDownloadTimer()
    {
        _downloadTimer?.Stop();
        _downloadTimer = null;
    }

    private void StartUpdateBannerPolling()
    {
        if (UpdateChecker.Latest != null) return; // check already completed

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            ticks++;
            if (UpdateChecker.Latest != null || ticks >= 10)
            {
                timer.Stop();
                RefreshUpdateBanner();
            }
        };
        timer.Start();
    }

    private void UpdateAction_Click(object sender, RoutedEventArgs e)
    {
        switch (UpdateChecker.State)
        {
            case UpdateChecker.DownloadState.Idle:
            case UpdateChecker.DownloadState.Error:
                UpdateChecker.ResetDownload();
                UpdateChecker.StartDownloadAsync();
                RefreshUpdateBanner();
                break;

            case UpdateChecker.DownloadState.Downloading:
                UpdateChecker.CancelDownload();
                RefreshUpdateBanner();
                break;

            case UpdateChecker.DownloadState.Ready:
                // H1: do not close Revit unless the installer actually started.
                if (!UpdateChecker.LaunchInstaller())
                {
                    RefreshUpdateBanner();
                    break;
                }
                RefreshUpdateBanner();
                // Close Revit after a short delay so the installer process has
                // time to start and enter its Assert-RevitClosed loop before
                // Revit exits. Without this the DLLs are locked and the install fails.
                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                closeTimer.Tick += (_, _) =>
                {
                    closeTimer.Stop();
                    try { System.Windows.Application.Current?.Shutdown(); } catch { }
                };
                closeTimer.Start();
                break;

            case UpdateChecker.DownloadState.Installing:
                // Button is disabled in this state — unreachable in practice.
                break;

            case UpdateChecker.DownloadState.Done:
                // Banner is hidden in this state; click is unreachable.
                break;

            default:
                break; // Guard against future enum additions.
        }
    }

    private void UpdateManual_Click(object sender, RoutedEventArgs e)
    {
        var info = UpdateChecker.Latest;
        if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl)) return;

        // H38: never shell-execute an unvalidated URL. UseShellExecute dispatches by
        // scheme, so a non-https FileName (file://, ms-msdt:, UNC path) that slipped
        // through would run as the user. Gate on the same HTTPS + host allowlist used
        // for the elevated installer path.
        if (!UpdateChecker.IsTrustedDownloadUrl(info.DownloadUrl))
        {
            TaskDialog.Show(Localization.T("support.title"),
                Localization.T("update.open_browser_failed", info.DownloadUrl));
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = info.DownloadUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            TaskDialog.Show(Localization.T("support.title"),
                Localization.T("update.open_browser_failed", ex.Message));
        }
    }

    private void RefreshConnectionStatus()
    {
        ApplyServerState(PluginHost.IsServiceRunning, PluginHost.Port ?? DefaultPort);
    }

    /// <summary>
    /// Paints the server card. Separate from the read so the UI preview
    /// renderer can show the "running" state without a Revit session.
    /// </summary>
    public void ApplyServerState(bool running, int port)
    {
        _serverRunning = running;

        // Orange = ON: the dot lights up with a halo. Off is plain grey.
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(running ? "Vx.Accent" : "Vx.StrokeStrong");
        StatusHalo.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        StatusTitle.Text = Localization.T(running ? "settings.server_on" : "settings.server_off");
        StatusDetail.Text = running
            ? Localization.T("settings.server_on_detail", port)
            : Localization.T("settings.server_off_detail");

        // The button offers the opposite state; starting is the accented one.
        ServerToggleButton.Content = Localization.T(running ? "settings.server_stop" : "settings.server_start");
        ServerToggleButton.Style = (Style)FindResource(running ? "Vx.Button" : "Vx.Button.Primary");
    }

    private void ServerToggle_Click(object sender, RoutedEventArgs e)
    {
        bool starting = !_serverRunning;
        if (starting) PluginHost.StartService();
        else PluginHost.StopService();

        // Start/stop raise ServiceStateChanged, which repaints the card. Read
        // the state again here so a start that did nothing (plugin not fully
        // initialized, port taken) is reported instead of silently ignored.
        RefreshConnectionStatus();
        if (starting && !_serverRunning)
            ShowSaveFeedback(Localization.T("settings.server_start_failed"), success: false, restartHint: true);
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonConvert.DeserializeObject<CortexSettings>(json);
                if (settings != null)
                {
                    _originalPort = settings.Port;
                    PortTextBox.Text = settings.Port.ToString();
                    SetLogLevel(settings.LogLevel ?? DefaultLogLevel);
                    ReadOnlyCheckBox.IsChecked = settings.ReadOnlyMode;
                    KeepCountTextBox.Text = ClampKeepCount(settings.SupportReportKeepCount).ToString();
                    EnableTelemetryCheckBox.IsChecked = settings.EnableTelemetry;
                    return;
                }
            }
        }
        catch { }

        SetDefaults();
    }

    private void SetDefaults()
    {
        _originalPort = DefaultPort;
        PortTextBox.Text = DefaultPort.ToString();
        SetLogLevel(DefaultLogLevel);
        ReadOnlyCheckBox.IsChecked = false;
        KeepCountTextBox.Text = DefaultKeepCount.ToString();
        EnableTelemetryCheckBox.IsChecked = false;
    }

    private static int ClampKeepCount(int n) => n < 1 ? 1 : (n > 200 ? 200 : n);

    // The four level names are the values stored in settings.json.
    private void SetLogLevel(string value)
    {
        if (value.Equals("Debug", StringComparison.OrdinalIgnoreCase)) LogDebug.IsChecked = true;
        else if (value.Equals("Warning", StringComparison.OrdinalIgnoreCase)) LogWarning.IsChecked = true;
        else if (value.Equals("Error", StringComparison.OrdinalIgnoreCase)) LogError.IsChecked = true;
        else LogInfo.IsChecked = true;
    }

    private string GetLogLevel()
    {
        if (LogDebug.IsChecked == true) return "Debug";
        if (LogWarning.IsChecked == true) return "Warning";
        if (LogError.IsChecked == true) return "Error";
        return DefaultLogLevel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortTextBox.Text.Trim(), out int port) || port < 1 || port > 65535)
        {
            // Inline, next to the Save button the user just pressed: no popup.
            ShowSaveFeedback(Localization.T("settings.invalid_port"), success: false, restartHint: true);
            PortTextBox.Focus();
            PortTextBox.SelectAll();
            return;
        }

        string logLevel = GetLogLevel();

        if (!int.TryParse(KeepCountTextBox.Text.Trim(), out int keep)) keep = DefaultKeepCount;
        keep = ClampKeepCount(keep);
        KeepCountTextBox.Text = keep.ToString();

        try
        {
            // Merge-write: preserve keys managed by other pages (e.g. EnableCodeExecution,
            // DisabledTools) by loading the existing JSON first and updating only our fields.
            JObject settings = File.Exists(SettingsFilePath)
                ? JObject.Parse(File.ReadAllText(SettingsFilePath))
                : new JObject();

            settings["Port"] = port;
            settings["LogLevel"] = logLevel;
            settings["ReadOnlyMode"] = ReadOnlyCheckBox.IsChecked == true;
            settings["SupportReportKeepCount"] = keep;
            // Never persist "enabled" while the fork has telemetry switched off,
            // even if an older settings.json (from upstream) had it on.
            settings["EnableTelemetry"] = ForkInfo.TelemetryEnabled
                && EnableTelemetryCheckBox.IsChecked == true;
            // Saving the page is an affirmative action: stamp consent so the
            // first-run dialog does not re-ask what the user just decided.
            settings["TelemetryConsentAnswered"] = true;
            settings["TelemetryConsentVersion"] =
                RevitCortex.Core.Telemetry.TelemetryConfig.CurrentConsentVersion;

            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(SettingsFilePath, settings.ToString(Formatting.Indented));

            // Apply read-only mode immediately (no restart needed)
            PluginHost.SetReadOnlyMode(ReadOnlyCheckBox.IsChecked == true);

            bool portChanged = port != _originalPort;
            if (portChanged)
            {
                ShowSaveFeedback(Localization.T("settings.saved_restart"), success: true, restartHint: true);
                _originalPort = port;
            }
            else
            {
                ShowSaveFeedback(Localization.T("settings.saved"), success: true);
            }
        }
        catch (Exception ex)
        {
            ShowSaveFeedback(Localization.T("settings.save_failed", ex.Message), success: false, restartHint: true);
        }
    }

    private void ShowSaveFeedback(string message, bool success, bool restartHint = false)
    {
        SaveFeedbackText.Text = message;
        // Amber = needs attention; a plain confirmation stays in the ink color.
        SaveFeedbackText.Foreground = (System.Windows.Media.Brush)FindResource(success ? "Vx.Ink" : "Vx.Caution");
        SaveFeedbackText.Visibility = Visibility.Visible;
        FooterHint.Visibility = Visibility.Collapsed;

        // Longer messages (restart hint, errors) stay 4 s so they can be read.
        var ttl = restartHint ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(2.5);

        _saveFeedbackTimer?.Stop();
        _saveFeedbackTimer = new DispatcherTimer { Interval = ttl };
        _saveFeedbackTimer.Tick += (_, _) =>
        {
            _saveFeedbackTimer?.Stop();
            _saveFeedbackTimer = null;
            SaveFeedbackText.Visibility = Visibility.Collapsed;
            FooterHint.Visibility = Visibility.Visible;
        };
        _saveFeedbackTimer.Start();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e) => SetDefaults();

    private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SendSupportReport.ReportsFolder);
            System.Diagnostics.Process.Start("explorer.exe", SendSupportReport.ReportsFolder);
        }
        catch (Exception ex)
        {
            TaskDialog.Show(Localization.T("support.title"),
                Localization.T("support.settings.open_folder_failed", ex.Message));
        }
    }

    private void DeleteAllReports_Click(object sender, RoutedEventArgs e)
    {
        var title = Localization.T("support.title");
        int count = SendSupportReport.CountReports();
        if (count == 0)
        {
            TaskDialog.Show(title, Localization.T("support.cleanup.none"));
            return;
        }

        long bytes = SendSupportReport.TotalReportsBytes();
        string size = FormatSize(bytes);
        var dialog = new TaskDialog(title)
        {
            MainInstruction = Localization.T("support.cleanup.confirm_title"),
            MainContent = Localization.T("support.cleanup.confirm_body", count, size),
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No,
        };
        if (dialog.Show() != TaskDialogResult.Yes) return;

        var (deleted, failed, _) = SendSupportReport.DeleteAllReports();
        if (failed == 0)
            TaskDialog.Show(title, Localization.T("support.cleanup.done", deleted));
        else
            TaskDialog.Show(title, Localization.T("support.cleanup.partial", deleted, failed));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}

internal class CortexSettings
{
    public int Port { get; set; } = CortexEnvironment.Current.DefaultPort;
    public string? LogLevel { get; set; } = "Info";
    public bool ReadOnlyMode { get; set; }
    public int SupportReportKeepCount { get; set; } = 10;
    public bool EnableTelemetry { get; set; }
}
