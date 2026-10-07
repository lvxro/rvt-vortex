using System.Windows;
using System.Windows.Automation;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.PowerBi;

public partial class ProfileNameDialog : Window
{
    public string ProfileName { get; private set; } = "";

    public ProfileNameDialog()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        Title = Localization.T("pbi.window_title");
        HeadingText.Text = Localization.T("pbi.profile_name.heading");
        HelpText.Text = Localization.T("pbi.profile_name.help");
        NameLabel.Text = Localization.T("pbi.profile_name.label");
        AutomationProperties.SetName(NameBox, Localization.T("pbi.profile_name.label"));
        CancelButton.Content = Localization.T("pbi.cancel");
        SaveButton.Content = Localization.T("pbi.save");

        Loaded += (_, _) => NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ProfileName = NameBox.Text.Trim();
        // An empty name keeps the dialog open rather than closing it as "cancelled".
        if (ProfileName.Length == 0)
        {
            NameBox.Focus();
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
