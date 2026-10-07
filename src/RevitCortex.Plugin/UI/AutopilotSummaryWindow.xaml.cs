using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// "What happened while you were away": shown when Autopilot stops after
/// having done something. Non-modal; closing it has no side effects.
/// </summary>
public partial class AutopilotSummaryWindow : Window
{
    /// <param name="snapshot">Final counters of the run that just ended.</param>
    /// <param name="stoppedAt">When Autopilot was turned off (local time).</param>
    /// <param name="ownerHandle">Revit's main window, or zero.</param>
    public AutopilotSummaryWindow(AutopilotSnapshot snapshot, DateTime stoppedAt, IntPtr ownerHandle)
    {
        InitializeComponent();
        snapshot = snapshot ?? AutopilotSnapshot.Empty;

        if (ownerHandle != IntPtr.Zero)
        {
            try { new WindowInteropHelper(this).Owner = ownerHandle; }
            catch { /* non-critical */ }
        }

        Title = Localization.T("win.title");
        TitleText.Text = Localization.T("summary.title");

        if (snapshot.StartedAt.HasValue)
        {
            DurationText.Text = Localization.T("summary.duration",
                AutoModeWindow.FormatDuration(stoppedAt - snapshot.StartedAt.Value),
                snapshot.StartedAt.Value.ToString("HH:mm"),
                stoppedAt.ToString("HH:mm"));
        }
        else
        {
            DurationText.Visibility = Visibility.Collapsed;
        }

        StatApproved.Text = snapshot.Approved.ToString();
        StatDeclined.Text = snapshot.Declined.ToString();
        if (snapshot.Declined > 0) StatDeclined.Foreground = (Brush)FindResource("Vx.Caution");
        StatDialogs.Text = snapshot.DialogsClosed.ToString();
        StatSaved.Text = snapshot.LastSavedAt.HasValue ? snapshot.LastSavedAt.Value.ToString("HH:mm") : "–";

        StatApprovedLabel.Text = Localization.T("panel.stat_approved");
        StatDeclinedLabel.Text = Localization.T("panel.stat_declined");
        StatDialogsLabel.Text = Localization.T("panel.stat_dialogs");
        StatSavedLabel.Text = Localization.T("panel.stat_saved");

        if (snapshot.DeclinedEvents.Count > 0)
        {
            PendingHeading.Text = Localization.T("summary.pending");
            PendingNote.Text = Localization.T("summary.pending_note");
            foreach (var e in snapshot.DeclinedEvents)
                PendingPanel.Children.Add(BuildPendingRow(e));
        }
        else
        {
            PendingSection.Visibility = Visibility.Collapsed;
            NothingPendingText.Text = Localization.T("summary.nothing_pending");
            NothingPendingText.Visibility = Visibility.Visible;
        }

        OpenLogButton.Content = Localization.T("summary.open_log");
        DoneButton.Content = Localization.T("summary.done");
    }

    private UIElement BuildPendingRow(AutopilotEvent e)
    {
        var row = new Grid { MinHeight = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var time = new TextBlock
        {
            Text = e.Time.ToString("HH:mm"),
            FontFamily = (FontFamily)FindResource("Vx.Mono"),
            FontSize = 11.5,
            Foreground = (Brush)FindResource("Vx.InkSoft"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(time, 0);

        var text = new TextBlock
        {
            Text = e.Text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5),
        };
        Grid.SetColumn(text, 1);

        row.Children.Add(time);
        row.Children.Add(text);
        return row;
    }

    private void Card_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { DragMove(); } catch (InvalidOperationException) { /* mouse released mid-drag */ }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e) => AutoModeWindow.OpenLogFile();

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
