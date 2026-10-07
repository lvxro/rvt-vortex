using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace RevitCortex.Plugin.UI;

public partial class SettingsWindow : Window
{
    private readonly GeneralSettingsPage _generalPage;
    private readonly ToolsSettingsPage _toolsPage;
    private bool _isInitialized;

    public SettingsWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        Title = Localization.T("settings.window_title");
        TitleText.Text = Localization.T("settings.title");
        GeneralTabText.Text = Localization.T("settings.tab_general");
        ToolsTabText.Text = Localization.T("settings.tab_tools");
        SubtitleText.Text = BuildSubtitle();

        int toolCount = PluginHost.ToolCount;
        ToolsCountText.Text = toolCount > 0 ? toolCount.ToString() : string.Empty;

        _generalPage = new GeneralSettingsPage();
        _toolsPage = new ToolsSettingsPage();

        ContentFrame.Navigate(_generalPage);
        _isInitialized = true;
    }

    /// <summary>Selects the Tools tab. Used by the UI preview renderer.</summary>
    public void ShowToolsTab() => ToolsTab.IsChecked = true;

    public GeneralSettingsPage GeneralPage => _generalPage;
    public ToolsSettingsPage ToolsPage => _toolsPage;

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        // Fires once during InitializeComponent, before the pages exist.
        if (!_isInitialized) return;

        if (ToolsTab.IsChecked == true)
            ContentFrame.Navigate(_toolsPage);
        else
            ContentFrame.Navigate(_generalPage);
    }

    /// <summary>"RVT Vortex 1.1.3 · Revit 2027", from the assembly and its install folder.</summary>
    private static string BuildSubtitle()
    {
        string version = "?";
        try
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null) version = v.Build >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
        }
        catch { /* keep the placeholder */ }

        // The plugin is installed under ...\Addins\<year>\RevitCortex, so the
        // folder after "Addins" is the Revit version it was loaded by.
        string? revitYear = null;
        try
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string[] parts = assemblyPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("Addins", StringComparison.OrdinalIgnoreCase))
                {
                    revitYear = parts[i + 1];
                    break;
                }
            }
        }
        catch { /* not installed in the usual place */ }

        return string.IsNullOrEmpty(revitYear)
            ? Localization.T("settings.subtitle_no_revit", version)
            : Localization.T("settings.subtitle", version, revitYear);
    }
}
