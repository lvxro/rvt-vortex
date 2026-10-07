using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using CoreSettings = RevitCortex.Core.Security.CortexSettings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RevitCortex.Plugin.UI;

public partial class ToolsSettingsPage : Page
{
    private enum ToolFilter { All, Enabled, Disabled }

    private readonly List<CategoryGroup> _allGroups = new();
    private readonly ObservableCollection<CategoryGroup> _shownGroups = new();
    private DispatcherTimer? _saveFeedbackTimer;
    private bool _toolsLoaded;
    private bool _isInitialized;

    private static string SettingsFilePath => CortexEnvironment.Current.SettingsFilePath;

    public ToolsSettingsPage()
    {
        InitializeComponent();
        ApplyLocalizedStrings();
        CategoryList.ItemsSource = _shownGroups;
        _isInitialized = true;

        var tools = PluginHost.GetAllToolInfo();
        if (tools != null) SetTools(tools);
        else ApplyFilter();

        // Reflect the persisted master switch for custom C# execution.
        try { CodeExecToggle.IsChecked = CoreSettings.Load().EnableCodeExecution; }
        catch { CodeExecToggle.IsChecked = false; }
    }

    private void ApplyLocalizedStrings()
    {
        SecScriptsTitle.Text = Localization.T("tools.sec_scripts");
        ScriptsToggleTitle.Text = Localization.T("tools.scripts_toggle");
        ScriptsHelp.Text = Localization.T("tools.scripts_help");

        ToolsHeading.Text = Localization.T("tools.heading");
        SearchPlaceholder.Text = Localization.T("tools.search");
        FilterAll.Content = Localization.T("tools.filter_all");
        FilterOn.Content = Localization.T("tools.filter_on");
        FilterOff.Content = Localization.T("tools.filter_off");

        FooterHint.Text = Localization.T("settings.footer_hint");
        EnableAllButton.Content = Localization.T("tools.enable_all");
        DisableAllButton.Content = Localization.T("tools.disable_all");
        SaveButton.Content = Localization.T("settings.save");
    }

    /// <summary>
    /// Fills the list. Called with the router's tools at construction; public
    /// so the UI preview renderer can show the page without a Revit session.
    /// </summary>
    public void SetTools(IEnumerable<(string Name, string Category, string Description, bool IsEnabled)> tools)
    {
        _allGroups.Clear();
        _toolsLoaded = true;

        foreach (var group in tools.GroupBy(t => t.Category).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var items = group
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => new ToolRowItem
                {
                    ToolName = t.Name,
                    Description = t.Description ?? string.Empty,
                    IsEnabled = t.IsEnabled,
                });
            var categoryGroup = new CategoryGroup(group.Key, items);
            categoryGroup.EnabledCountChanged += UpdateCount;
            _allGroups.Add(categoryGroup);
        }

        ApplyFilter();
    }

    private ToolFilter CurrentFilter =>
        FilterOn.IsChecked == true ? ToolFilter.Enabled
        : FilterOff.IsChecked == true ? ToolFilter.Disabled
        : ToolFilter.All;

    private void ApplyFilter()
    {
        string query = SearchBox.Text.Trim();
        var filter = CurrentFilter;

        // A search, or asking for what is switched off, is a question about
        // specific tools: open the groups that answer it. Plain browsing keeps
        // whatever the user opened by hand.
        bool openMatches = query.Length > 0 || filter == ToolFilter.Disabled;

        _shownGroups.Clear();
        foreach (var group in _allGroups)
        {
            bool groupNameMatches = query.Length > 0 && Contains(group.CategoryName, query);
            var visible = group.Tools.Where(t =>
                (filter == ToolFilter.All
                    || (filter == ToolFilter.Enabled && t.IsEnabled)
                    || (filter == ToolFilter.Disabled && !t.IsEnabled))
                && (query.Length == 0
                    || groupNameMatches
                    || Contains(t.ToolName, query)
                    || Contains(t.Description, query)));

            group.Show(visible, openMatches, isFirst: _shownGroups.Count == 0);
            if (group.VisibleTools.Count > 0) _shownGroups.Add(group);
        }

        UpdateEmptyState(query.Length > 0 || filter != ToolFilter.All);
        UpdateCount();
    }

    private static bool Contains(string text, string query) =>
        text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private void UpdateEmptyState(bool filtering)
    {
        if (_shownGroups.Count > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        if (!_toolsLoaded || _allGroups.Count == 0)
        {
            EmptyTitle.Text = Localization.T("tools.none_loaded");
            EmptyHelp.Text = Localization.T("tools.none_loaded_help");
        }
        else
        {
            EmptyTitle.Text = Localization.T("tools.no_match");
            EmptyHelp.Text = filtering ? Localization.T("tools.no_match_help") : string.Empty;
        }
        EmptyState.Visibility = Visibility.Visible;
    }

    private void UpdateCount()
    {
        int total = 0, enabled = 0;
        foreach (var group in _allGroups)
        {
            total += group.TotalCount;
            enabled += group.EnabledCount;
        }
        ToolCountText.Text = total == 0 ? string.Empty : Localization.T("tools.count", enabled, total);
    }

    // ── Events ───────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Fires while InitializeComponent is still building the page.
        if (!_isInitialized) return;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        ApplyFilter();
    }

    private void GroupHeader_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CategoryGroup group)
            group.ToggleExpanded();
    }

    private void GroupAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CategoryGroup group)
            group.SetAll(enabled: group.HasDisabled);
    }

    private void EnableAll_Click(object sender, RoutedEventArgs e) => SetAll(true);

    private void DisableAll_Click(object sender, RoutedEventArgs e) => SetAll(false);

    private void SetAll(bool enabled)
    {
        foreach (var group in _allGroups) group.SetAll(enabled);
        // With the "Enabled" / "Disabled" filter on, the visible set just changed.
        if (CurrentFilter != ToolFilter.All) ApplyFilter();
    }

    private void ListCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Rows are square; clip them to the card's rounded corners.
        ListCard.Clip = new RectangleGeometry(
            new Rect(0, 0, ListCard.ActualWidth, ListCard.ActualHeight), 12, 12);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var disabledTools = _allGroups
                .SelectMany(g => g.Tools)
                .Where(t => !t.IsEnabled)
                .Select(t => t.ToolName)
                .ToArray();

            // Nothing loaded (no Revit session): keep whatever list is on disk
            // rather than overwrite it with "nothing is disabled".
            if (_toolsLoaded)
            {
                JObject settings = File.Exists(SettingsFilePath)
                    ? JObject.Parse(File.ReadAllText(SettingsFilePath))
                    : new JObject();

                settings["DisabledTools"] = new JArray(disabledTools);

                string dir = Path.GetDirectoryName(SettingsFilePath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsFilePath, settings.ToString(Formatting.Indented));

                PluginHost.SetDisabledTools(disabledTools);
            }

            // Persist the master switch for send_code_to_revit (merge-write preserves
            // DisabledTools just written above and any other keys).
            bool codeExec = CodeExecToggle.IsChecked == true;
            CoreSettings.SetEnableCodeExecution(codeExec);

            ShowSaveFeedback(
                Localization.T(codeExec ? "tools.saved_scripts_on" : "tools.saved", disabledTools.Length),
                success: true);
        }
        catch (Exception ex)
        {
            ShowSaveFeedback(Localization.T("settings.save_failed", ex.Message), success: false);
        }
    }

    private void ShowSaveFeedback(string message, bool success)
    {
        SaveFeedbackText.Text = message;
        // Amber = needs attention; a plain confirmation stays in the ink color.
        SaveFeedbackText.Foreground = (Brush)FindResource(success ? "Vx.Ink" : "Vx.Caution");
        SaveFeedbackText.Visibility = Visibility.Visible;
        FooterHint.Visibility = Visibility.Collapsed;

        _saveFeedbackTimer?.Stop();
        _saveFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _saveFeedbackTimer.Tick += (_, _) =>
        {
            _saveFeedbackTimer?.Stop();
            _saveFeedbackTimer = null;
            SaveFeedbackText.Visibility = Visibility.Collapsed;
            FooterHint.Visibility = Visibility.Visible;
        };
        _saveFeedbackTimer.Start();
    }
}

/// <summary>
/// One category in the tools list. Always holds every tool of the category;
/// <see cref="VisibleTools"/> is the subset the current search/filter shows.
/// The count and the group action are about the whole category either way.
/// </summary>
public class CategoryGroup : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _openedByFilter;
    private Thickness _headerBorder;

    public CategoryGroup(string categoryName, IEnumerable<ToolRowItem> tools)
    {
        CategoryName = categoryName;
        Tools = tools.ToList();
        foreach (var tool in Tools)
            tool.PropertyChanged += OnToolChanged;
    }

    public string CategoryName { get; }
    public IReadOnlyList<ToolRowItem> Tools { get; }
    public ObservableCollection<ToolRowItem> VisibleTools { get; } = new();

    public int EnabledCount => Tools.Count(t => t.IsEnabled);
    public int TotalCount => Tools.Count;
    public bool HasDisabled => EnabledCount < TotalCount;

    /// <summary>"7 of 7"</summary>
    public string CountText => Localization.T("tools.group_count", EnabledCount, TotalCount);

    /// <summary>Offers the opposite of the group's state: enable if anything is off.</summary>
    public string GroupActionText =>
        Localization.T(HasDisabled ? "tools.group_enable" : "tools.group_disable");

    /// <summary>Open by hand, or opened because the search/filter matched inside.</summary>
    public bool ShowTools => _isExpanded || _openedByFilter;

    /// <summary>Hairline above the header, except on the first group of the list.</summary>
    public Thickness HeaderBorder
    {
        get => _headerBorder;
        private set
        {
            if (_headerBorder == value) return;
            _headerBorder = value;
            Raise(nameof(HeaderBorder));
        }
    }

    /// <summary>Raised when a tool of this group is switched on or off.</summary>
    public event Action? EnabledCountChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Applies a search/filter result to this group.</summary>
    public void Show(IEnumerable<ToolRowItem> visibleTools, bool openMatches, bool isFirst)
    {
        var list = visibleTools.ToList();
        VisibleTools.Clear();
        foreach (var tool in list) VisibleTools.Add(tool);

        HeaderBorder = isFirst ? new Thickness(0) : new Thickness(0, 1, 0, 0);

        if (_openedByFilter != openMatches)
        {
            _openedByFilter = openMatches;
            Raise(nameof(ShowTools));
        }
    }

    public void ToggleExpanded()
    {
        // One click always changes what the user sees, including on a group
        // the search opened.
        bool show = !ShowTools;
        _isExpanded = show;
        _openedByFilter = false;
        Raise(nameof(ShowTools));
    }

    public void SetAll(bool enabled)
    {
        foreach (var tool in Tools) tool.IsEnabled = enabled;
    }

    private void OnToolChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToolRowItem.IsEnabled)) return;
        Raise(nameof(EnabledCount));
        Raise(nameof(HasDisabled));
        Raise(nameof(CountText));
        Raise(nameof(GroupActionText));
        EnabledCountChanged?.Invoke();
    }

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class ToolRowItem : INotifyPropertyChanged
{
    private bool _isEnabled;

    public string ToolName { get; set; } = "";
    public string Description { get; set; } = "";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
