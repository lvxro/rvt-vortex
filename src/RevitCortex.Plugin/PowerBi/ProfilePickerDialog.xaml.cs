using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.PowerBi;

public partial class ProfilePickerDialog : Window
{
    private readonly ObservableCollection<ProfileListRow> _rows = new ObservableCollection<ProfileListRow>();

    public PowerBiExportProfile? SelectedProfile { get; private set; }

    public ProfilePickerDialog(List<PowerBiExportProfile> profiles)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        Title = Localization.T("pbi.window_title");
        HeadingText.Text = Localization.T("pbi.profile_picker.heading");
        HelpText.Text = Localization.T("pbi.profile_picker.help");
        EmptyText.Text = Localization.T("pbi.profile_picker.empty");
        DeleteButton.Content = Localization.T("pbi.profile_picker.delete");
        CancelButton.Content = Localization.T("pbi.cancel");
        LoadButton.Content = Localization.T("pbi.profile_picker.load");

        foreach (var profile in profiles) _rows.Add(new ProfileListRow(profile));
        ProfileList.ItemsSource = _rows;
        if (_rows.Count > 0) ProfileList.SelectedIndex = 0;
        RefreshState();
    }

    private void RefreshState()
    {
        bool any = _rows.Count > 0;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.IsEnabled = any;
        LoadButton.IsEnabled = any;
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        if (!(ProfileList.SelectedItem is ProfileListRow row)) return;
        SelectedProfile = row.Profile;
        DialogResult = true;
    }

    private void ProfileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only on a row: not on the scroll bar or the empty space under the list.
        if (!(e.OriginalSource is DependencyObject source)) return;
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(ProfileList, source) == null) return;
        Load_Click(sender, e);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!(ProfileList.SelectedItem is ProfileListRow row)) return;

        int index = ProfileList.SelectedIndex;
        ProfileStore.Delete(row.Profile.Name);
        _rows.Remove(row);
        if (_rows.Count > 0) ProfileList.SelectedIndex = System.Math.Min(index, _rows.Count - 1);
        RefreshState();
    }

    private void ListCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Rows are square; clip them to the card's rounded corners.
        ListCard.Clip = new RectangleGeometry(
            new Rect(0, 0, ListCard.ActualWidth, ListCard.ActualHeight), 12, 12);
    }

    /// <summary>One line of the list. Public so WPF binding can read it.</summary>
    public class ProfileListRow
    {
        public PowerBiExportProfile Profile { get; }
        public string Name { get; }
        public string Detail { get; }
        public string When { get; }

        public ProfileListRow(PowerBiExportProfile profile)
        {
            Profile = profile;
            Name = profile.Name;
            When = profile.LastUsed.ToLocalTime().ToString("g");

            if (profile.UseSchedules)
            {
                Detail = Plural("pbi.count.schedules", profile.ScheduleIds?.Count ?? 0);
            }
            else
            {
                // A hand-edited profile may leave a list out.
                int columns = (profile.InstanceParameters?.Count ?? 0) + (profile.TypeParameters?.Count ?? 0);
                Detail = Plural("pbi.count.categories", profile.Categories?.Count ?? 0) + " · "
                       + Plural("pbi.count.columns", columns);
            }
        }

        private static string Plural(string key, int count)
            => Localization.T(count == 1 ? key + "_one" : key + "_many", count);
    }
}
