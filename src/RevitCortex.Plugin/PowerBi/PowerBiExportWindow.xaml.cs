using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RevitCortex.Plugin.UI;
// System.Windows has a Localization class too.
using Localization = RevitCortex.Plugin.UI.Localization;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Power BI export, in two steps: what to export (step 1), then where it goes
/// and how it stays up to date (step 2).
///
/// The window is a view. Everything it needs from Revit comes through
/// <see cref="IPowerBiExportSource"/>, so it can be built without Revit: the
/// UI preview tests give it sample data. Do not reference the Revit API or
/// RevitCortexApp from this file.
/// </summary>
public partial class PowerBiExportWindow : Window
{
    private enum SourceMode { Categories, Schedules }
    private enum SchemaMode { Auto, Suggested, Custom }

    /// <summary>Rows shown in the preview of step 2.</summary>
    private const int PreviewRowCount = 5;

    private readonly IPowerBiExportSource _source;

    // Step 1: categories (split by Revit's own grouping) and schedules.
    private readonly List<CategoryRow> _allCategories = new List<CategoryRow>();
    private readonly ObservableCollection<CategoryRow> _modelCategories = new ObservableCollection<CategoryRow>();
    private readonly ObservableCollection<CategoryRow> _annotationCategories = new ObservableCollection<CategoryRow>();
    private readonly ObservableCollection<CategoryRow> _analyticalCategories = new ObservableCollection<CategoryRow>();
    private readonly ObservableCollection<CategoryRow> _otherCategories = new ObservableCollection<CategoryRow>();
    private readonly ObservableCollection<ScheduleRow> _schedules = new ObservableCollection<ScheduleRow>();
    private readonly ObservableCollection<ScheduleFieldRow> _scheduleFields = new ObservableCollection<ScheduleFieldRow>();

    // Step 1: parameters of the chosen categories, and the ones that become columns.
    private readonly List<ParameterRow> _allParameters = new List<ParameterRow>();
    private readonly ObservableCollection<ParameterRow> _availableParams = new ObservableCollection<ParameterRow>();
    private readonly ObservableCollection<ParameterRow> _selectedParams = new ObservableCollection<ParameterRow>();

    // Step 2, advanced: one entry per CSV column.
    private readonly ObservableCollection<ColumnTypeMapping> _columnTypes = new ObservableCollection<ColumnTypeMapping>();

    private SourceMode _mode = SourceMode.Categories;
    private PowerBiScope _scope = PowerBiScope.WholeModel;
    private SchemaMode _schemaMode = SchemaMode.Auto;
    private int _step = 1;

    private bool _ready;          // InitializeComponent finished: handlers may run
    private bool _loaded;         // the model has been read
    private bool _bulk;           // many rows are being changed at once: react once, afterwards
    private bool _advancedOpen;
    private List<string>? _pendingColumns;   // columns of a profile, waiting for the parameters to load
    private DispatcherTimer? _paramLoadTimer;
    private string? _status;
    private bool _statusNeedsAttention;

    public PowerBiExportWindow(IPowerBiExportSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));

        InitializeComponent();
        DarkTitleBar.Apply(this);
        ApplyLocalizedStrings();

        CategoryList.ItemsSource = _modelCategories;
        ScheduleList.ItemsSource = _schedules;
        ScheduleFieldsList.ItemsSource = _scheduleFields;
        AvailableList.ItemsSource = _availableParams;
        SelectedList.ItemsSource = _selectedParams;
        ColumnTypesList.ItemsSource = _columnTypes;

        _ready = true;
        ShowStep(1);
        RefreshScheduleFields(null);
        ApplyParameterFilter();

        Loaded += (_, _) => LoadFromSource();
        Closing += (_, _) => _paramLoadTimer?.Stop();
    }

    // ───────────────────────── Texts ─────────────────────────

    private static string T(string key) => Localization.T(key);
    private static string T(string key, params object?[] args) => Localization.T(key, args);

    /// <summary>"1 category" / "4 categories": the key carries _one and _many forms.</summary>
    private static string Counted(string key, int count)
        => Localization.T(count == 1 ? key + "_one" : key + "_many", count);

    private void ApplyLocalizedStrings()
    {
        Title = T("pbi.window_title");
        TitleText.Text = T("pbi.title");

        StepDataCurrent.Text = T("pbi.step_data");
        StepDataDone.Text = T("pbi.step_data");
        StepOutputPending.Text = T("pbi.step_output");
        StepOutputCurrent.Text = T("pbi.step_output");

        ProfilesBtnText.Text = T("pbi.profiles");
        MenuLoadProfile.Content = T("pbi.profiles.load");
        MenuSaveProfile.Content = T("pbi.profiles.save");
        MenuImportProfile.Content = T("pbi.profiles.import");
        MenuOpenProfiles.Content = T("pbi.profiles.open_folder");
        SaveProfileBtn.Content = T("pbi.profiles.save");

        ScopeWholeTitle.Text = T("pbi.scope.whole");
        ScopeWholeHelp.Text = T("pbi.scope.whole_help");
        ScopeViewTitle.Text = T("pbi.scope.view");
        ScopeViewHelp.Text = T("pbi.scope.view_help");
        ScopeSelectionTitle.Text = T("pbi.scope.selection");
        ScopeSelectionHelp.Text = T("pbi.scope.selection_help");
        ModeSchedulesTitle.Text = T("pbi.scope.schedules");
        ModeSchedulesHelp.Text = T("pbi.scope.schedules_help");
        ScopeWholeRadio.ToolTip = ScopeWholeHelp.Text;
        ScopeViewRadio.ToolTip = ScopeViewHelp.Text;
        ScopeSelectionRadio.ToolTip = ScopeSelectionHelp.Text;
        ModeSchedulesRadio.ToolTip = ModeSchedulesHelp.Text;

        CategoriesTitle.Text = T("pbi.categories");
        TabModel.Content = T("pbi.categories.model");
        TabAnnotation.Content = T("pbi.categories.annotation");
        TabAnalytical.Content = T("pbi.categories.analytical");
        TabOther.Content = T("pbi.categories.other");
        CategoriesAllBtn.Content = T("pbi.select_all");
        CategoriesNoneBtn.Content = T("pbi.select_none");

        AvailableTitle.Text = T("pbi.available");
        ParameterFilterPlaceholder.Text = T("pbi.filter");
        ParameterFilter.ToolTip = T("pbi.filter_tip");
        AutomationProperties.SetName(ParameterFilter, T("pbi.filter_tip"));
        IncludeTypeParametersBox.Content = T("pbi.include_type");
        IncludeTypeParametersBox.ToolTip = T("pbi.include_type_tip");
        HideEmptyBox.Content = T("pbi.hide_empty");
        HideEmptyBox.ToolTip = T("pbi.hide_empty_tip");

        Describe(AddBtn, T("pbi.move.add"));
        Describe(AddAllBtn, T("pbi.move.add_all"));
        Describe(RemoveBtn, T("pbi.move.remove"));
        Describe(RemoveAllBtn, T("pbi.move.remove_all"));
        Describe(MoveUpBtn, T("pbi.move.up"));
        Describe(MoveDownBtn, T("pbi.move.down"));

        SelectedTitle.Text = T("pbi.columns");
        SelectedEmpty.Text = T("pbi.columns.empty");

        SchedulesTitle.Text = T("pbi.schedules");
        SchedulesEmpty.Text = T("pbi.schedules.empty");
        SchedulesAllBtn.Content = T("pbi.select_all");
        SchedulesNoneBtn.Content = T("pbi.select_none");

        SecFileTitle.Text = T("pbi.file");
        SecFileHelp.Text = T("pbi.file_help");
        OutputFolderLabel.Text = T("pbi.output_folder");
        AutomationProperties.SetName(OutputFolderBox, T("pbi.output_folder"));
        BrowseBtn.Content = T("pbi.browse");
        BrowseBtn.ToolTip = T("pbi.browse_tip");
        OpenFolderBtn.Content = T("pbi.open");
        OpenFolderBtn.ToolTip = T("pbi.open_tip");
        FileNameLabel.Text = T("pbi.file_name");
        AutomationProperties.SetName(FileNameBox, T("pbi.file_name"));
        ScheduleFilesNote.Text = T("pbi.schedule_files_note");

        SecUpdateTitle.Text = T("pbi.update");
        SecUpdateHelp.Text = T("pbi.update_help");
        OverwriteTitle.Text = T("pbi.overwrite");
        OverwriteHelp.Text = T("pbi.overwrite_help");
        AutoExportTitle.Text = T("pbi.auto_export");
        AutoExportHelp.Text = T("pbi.auto_export_help");

        SecPowerBiTitle.Text = T("pbi.powerbi");
        SecPowerBiHelp.Text = T("pbi.powerbi_help");
        RegisterProtocolTitle.Text = T("pbi.select_in_revit");
        RegisterProtocolHelp.Text = T("pbi.select_in_revit_help");
        TriggerRefreshTitle.Text = T("pbi.refresh");
        TriggerRefreshHelp.Text = T("pbi.refresh_help");
        RefreshIdsHelp.Text = T("pbi.refresh_ids_help");
        RefreshWorkspaceIdBox.ToolTip = T("pbi.workspace_tip");
        RefreshDatasetIdBox.ToolTip = T("pbi.dataset_tip");

        PreviewTitle.Text = T("pbi.preview");

        AdvancedTitle.Text = T("pbi.advanced");
        AdvancedHelp.Text = T("pbi.advanced_help");
        SchemaModeAutoRadio.Content = T("pbi.schema.auto");
        SchemaModeSuggestedRadio.Content = T("pbi.schema.suggested");
        SchemaModeCustomRadio.Content = T("pbi.schema.custom");
        AdvancedModeText.Text = T("pbi.schema.auto");
        ColumnHeaderName.Text = T("pbi.schema.column");
        ColumnHeaderType.Text = T("pbi.schema.type");
        ColumnHeaderFormat.Text = T("pbi.schema.format");
        SuggestTypesBtn.Content = T("pbi.schema.suggest");

        BackBtn.Content = T("pbi.back");
        NextBtn.Content = T("pbi.next");
        ExportBtn.Content = T("pbi.export");
    }

    private static void Describe(FrameworkElement element, string text)
    {
        element.ToolTip = text;
        AutomationProperties.SetName(element, text);
    }

    // ───────────────────────── Loading the model ─────────────────────────

    /// <summary>
    /// Reads categories and schedules from the source. Runs once, when the
    /// window is first shown; public so the UI preview renderer can fill the
    /// window without showing it.
    /// </summary>
    public void LoadFromSource()
    {
        if (_loaded) return;
        _loaded = true;

        DebugLog("=== LoadFromSource ===");
        try { OutputFolderBox.Text = SuggestDefaultOutputFolder(); }
        catch (Exception ex) { DebugLog($"Default output folder failed: {ex.Message}"); }

        List<CategoryInfo> categories;
        try
        {
            categories = _source.DiscoverCategories();
            DebugLog($"DiscoverCategories: {categories.Count}");
        }
        catch (Exception ex)
        {
            DebugLog($"DiscoverCategories FAILED: {ex}");
            SetStatus(T("pbi.status.categories_failed", ex.Message), attention: true);
            _source.ShowError(T("pbi.dialog.categories_failed"), $"{ex.GetType().Name}: {ex.Message}", ex.StackTrace);
            return;
        }

        var schedules = new List<ScheduleInfo>();
        try
        {
            schedules = _source.DiscoverSchedules();
            DebugLog($"DiscoverSchedules: {schedules.Count}");
        }
        catch (Exception ex)
        {
            // Not blocking: the categories still work.
            DebugLog($"DiscoverSchedules FAILED: {ex.GetType().Name}: {ex.Message}");
        }

        _allCategories.Clear();
        foreach (var info in categories)
        {
            var row = new CategoryRow(info);
            row.PropertyChanged += (_, e) => OnCategoryRowChanged(e);
            _allCategories.Add(row);
        }

        _schedules.Clear();
        foreach (var info in schedules)
        {
            var row = new ScheduleRow(info, Counted("pbi.rows", info.RowCount));
            row.PropertyChanged += (_, e) => OnScheduleRowChanged(row, e);
            _schedules.Add(row);
        }
        SchedulesEmpty.Visibility = _schedules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ApplyCategoryFilter();
        RefreshCounts();

        if (categories.Count == 0) SetStatus(T("pbi.status.no_categories"), attention: true);
        else ClearStatus();
    }

    // ───────────────────────── Source: scope or schedules ─────────────────────────

    private void SourceScope_Changed(object sender, RoutedEventArgs e)
    {
        // Fires while the XAML is still being read, and for every radio a
        // profile sets: react once, when things are in place.
        if (!_ready || _bulk) return;
        ApplySource();
    }

    private void ApplySource()
    {
        if (ModeSchedulesRadio.IsChecked == true)
        {
            _mode = SourceMode.Schedules;
            _scope = PowerBiScope.WholeModel;
        }
        else
        {
            _mode = SourceMode.Categories;
            if (ScopeViewRadio.IsChecked == true) _scope = PowerBiScope.ActiveView;
            else if (ScopeSelectionRadio.IsChecked == true) _scope = PowerBiScope.Selection;
            else _scope = PowerBiScope.WholeModel;
        }

        bool schedules = _mode == SourceMode.Schedules;
        CategoryPanes.Visibility = schedules ? Visibility.Collapsed : Visibility.Visible;
        SchedulePanes.Visibility = schedules ? Visibility.Visible : Visibility.Collapsed;

        ClearStatus();
        if (!schedules)
        {
            RefreshScopeFilter();
            // What is in scope changed, so may the chosen categories.
            if (_loaded) LoadParametersNow();
        }
        RefreshCounts();
    }

    /// <summary>Marks which categories have elements in the active view or the selection.</summary>
    private void RefreshScopeFilter()
    {
        if (_scope == PowerBiScope.WholeModel)
        {
            foreach (var c in _allCategories) c.InScope = true;
        }
        else
        {
            PowerBiScopeFilter? filter = null;
            try
            {
                filter = _source.ReadScope(_scope, _allCategories.Select(c => c.OstCode).ToList());
            }
            catch (Exception ex)
            {
                DebugLog($"ReadScope failed: {ex.Message}");
            }

            foreach (var c in _allCategories)
                c.InScope = filter != null && filter.Categories.Contains(c.OstCode);

            // Otherwise the list just goes blank with no explanation.
            if (_scope == PowerBiScope.Selection && filter != null && filter.SelectionEmpty)
                SetStatus(T("pbi.status.selection_empty"), attention: true);
        }

        ApplyCategoryFilter();
    }

    // ───────────────────────── Step navigation ─────────────────────────

    private void Next_Click(object sender, RoutedEventArgs e) => GoToOutput();

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        ClearStatus();
        ShowStep(1);
    }

    /// <summary>
    /// Moves to step 2 when something is chosen to export. False (and a
    /// message in the footer) when there is nothing yet.
    /// </summary>
    public bool GoToOutput()
    {
        if (_mode == SourceMode.Categories)
        {
            if (SelectedCategories().Count == 0) { SetStatus(T("pbi.need_category"), attention: true); return false; }
            if (_selectedParams.Count == 0) { SetStatus(T("pbi.need_parameter"), attention: true); return false; }
        }
        else if (SelectedSchedules().Count == 0)
        {
            SetStatus(T("pbi.need_schedule"), attention: true);
            return false;
        }

        ClearStatus();
        ShowStep(2);

        bool schedules = _mode == SourceMode.Schedules;
        // The tool writes one schedule_<name>.csv per schedule and ignores a
        // file name: show the files it will write instead of asking for one.
        FileNamePanel.Visibility = schedules ? Visibility.Collapsed : Visibility.Visible;
        OverwriteBox.Visibility = schedules ? Visibility.Collapsed : Visibility.Visible;
        ScheduleFilesPanel.Visibility = schedules ? Visibility.Visible : Visibility.Collapsed;
        AdvancedCard.Visibility = schedules ? Visibility.Collapsed : Visibility.Visible;

        if (schedules)
        {
            SetAdvancedOpen(false);
            PopulateScheduleFilesList();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(FileNameBox.Text))
                FileNameBox.Text = SuggestFileName();
            // Keep the column types in step with the columns just chosen;
            // types already set on columns that are still there are kept.
            SyncColumnTypesWithSelection();
        }

        RefreshPreview();
        return true;
    }

    private void ShowStep(int step)
    {
        _step = step;
        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepperOnData.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        StepperOnOutput.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        SubtitleText.Text = step == 1 ? T("pbi.subtitle_data") : T("pbi.subtitle_output");

        BackBtn.IsEnabled = step == 2;
        NextBtn.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        ExportBtn.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        SaveProfileBtn.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFooter();
    }

    // ───────────────────────── Categories ─────────────────────────

    private List<CategoryRow> SelectedCategories()
        => _allCategories.Where(c => c.IsSelected && c.InScope).ToList();

    private void ApplyCategoryFilter()
    {
        _modelCategories.Clear();
        _annotationCategories.Clear();
        _analyticalCategories.Clear();
        _otherCategories.Clear();

        foreach (var c in _allCategories.Where(c => c.InScope))
        {
            switch (c.CategoryType)
            {
                case "Model": _modelCategories.Add(c); break;
                case "Annotation": _annotationCategories.Add(c); break;
                case "Analytical": _analyticalCategories.Add(c); break;
                default: _otherCategories.Add(c); break;
            }
        }

        // "Other" is a safety net for category types Revit may add; it only
        // shows when it has something in it.
        bool hasOther = _otherCategories.Count > 0;
        TabOther.Visibility = hasOther ? Visibility.Visible : Visibility.Collapsed;
        if (!hasOther && TabOther.IsChecked == true) TabModel.IsChecked = true;

        RefreshCategoryTab();
    }

    private void CategoryTab_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        RefreshCategoryTab();
    }

    private ObservableCollection<CategoryRow> ActiveCategoryTab()
    {
        if (TabAnnotation.IsChecked == true) return _annotationCategories;
        if (TabAnalytical.IsChecked == true) return _analyticalCategories;
        if (TabOther.IsChecked == true) return _otherCategories;
        return _modelCategories;
    }

    private void RefreshCategoryTab()
    {
        var rows = ActiveCategoryTab();
        if (!ReferenceEquals(CategoryList.ItemsSource, rows)) CategoryList.ItemsSource = rows;

        CategoriesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CategoriesEmpty.Text = !_loaded || _allCategories.Count == 0
            ? T("pbi.categories.none_in_model")
            : T("pbi.categories.none_here");

        TabModel.ToolTip = TabTip(_modelCategories);
        TabAnnotation.ToolTip = TabTip(_annotationCategories);
        TabAnalytical.ToolTip = TabTip(_analyticalCategories);
        TabOther.ToolTip = TabTip(_otherCategories);
    }

    private static string TabTip(ObservableCollection<CategoryRow> rows)
        => Counted("pbi.count.categories", rows.Count) + " · "
         + Counted("pbi.count.chosen_f", rows.Count(c => c.IsSelected));

    private void OnCategoryRowChanged(PropertyChangedEventArgs e)
    {
        if (_bulk || e.PropertyName != nameof(ViewRow.IsSelected)) return;
        ClearStatus();
        RefreshCategoryTab();
        RefreshCounts();
        ScheduleParamLoad();
    }

    private void SelectAllCategories_Click(object sender, RoutedEventArgs e) => SetActiveTab(selected: true);

    private void SelectNoneCategories_Click(object sender, RoutedEventArgs e) => SetActiveTab(selected: false);

    private void SetActiveTab(bool selected)
    {
        _bulk = true;
        try { foreach (var c in ActiveCategoryTab()) c.IsSelected = selected; }
        finally { _bulk = false; }

        ClearStatus();
        RefreshCategoryTab();
        RefreshCounts();
        ScheduleParamLoad();
    }

    // ───────────────────────── Schedules ─────────────────────────

    private List<ScheduleRow> SelectedSchedules()
        => _schedules.Where(s => s.IsSelected).ToList();

    private void OnScheduleRowChanged(ScheduleRow row, PropertyChangedEventArgs e)
    {
        if (_bulk || e.PropertyName != nameof(ViewRow.IsSelected)) return;
        ClearStatus();
        // Show the columns of the one just ticked; when it was unticked, of
        // the first that still is.
        RefreshScheduleFields(row.IsSelected ? row : SelectedSchedules().FirstOrDefault());
        RefreshCounts();
    }

    private void SelectAllSchedules_Click(object sender, RoutedEventArgs e) => SetAllSchedules(selected: true);

    private void SelectNoneSchedules_Click(object sender, RoutedEventArgs e) => SetAllSchedules(selected: false);

    private void SetAllSchedules(bool selected)
    {
        _bulk = true;
        try { foreach (var s in _schedules) s.IsSelected = selected; }
        finally { _bulk = false; }

        ClearStatus();
        RefreshScheduleFields(SelectedSchedules().FirstOrDefault());
        RefreshCounts();
    }

    private void RefreshScheduleFields(ScheduleRow? target)
    {
        _scheduleFields.Clear();
        ScheduleFieldsCount.Text = "";

        if (target == null)
        {
            ScheduleFieldsTitle.Text = T("pbi.schedule_columns");
            ShowScheduleFieldsMessage(T("pbi.schedule_columns.pick"));
            return;
        }

        List<ScheduleFieldInfo>? fields = null;
        try { fields = _source.GetScheduleFields(target.ScheduleId); }
        catch (Exception ex) { DebugLog($"GetScheduleFields failed: {ex.Message}"); }

        ScheduleFieldsTitle.Text = T("pbi.schedule_columns_of", target.Name);
        if (fields == null)
        {
            ShowScheduleFieldsMessage(T("pbi.schedule_columns.unreadable"));
            return;
        }

        foreach (var field in fields)
        {
            string badge = field.IsReadOnly ? T("pbi.badge.calculated")
                         : field.Scope == "Type" ? T("pbi.badge.type")
                         : "";
            _scheduleFields.Add(new ScheduleFieldRow(field.Header, badge));
        }

        ScheduleFieldsCount.Text = fields.Count.ToString();
        if (fields.Count == 0) ShowScheduleFieldsMessage(T("pbi.schedule_columns.none"));
        else ScheduleFieldsEmpty.Visibility = Visibility.Collapsed;
    }

    private void ShowScheduleFieldsMessage(string text)
    {
        ScheduleFieldsEmpty.Text = text;
        ScheduleFieldsEmpty.Visibility = Visibility.Visible;
    }

    // ───────────────────────── Parameters ─────────────────────────

    /// <summary>
    /// Reads the parameters a moment after the last category was ticked, so
    /// ticking several in a row reads the model once.
    /// </summary>
    private void ScheduleParamLoad()
    {
        if (_paramLoadTimer == null)
        {
            _paramLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _paramLoadTimer.Tick += (_, _) => LoadParametersNow();
        }
        _paramLoadTimer.Stop();
        _paramLoadTimer.Start();
    }

    private void LoadParametersNow()
    {
        _paramLoadTimer?.Stop();

        // Columns to keep, in the user's order: the ones of a profile just
        // loaded, otherwise the ones already chosen.
        var keep = _pendingColumns ?? _selectedParams.Select(p => p.Key).ToList();
        _pendingColumns = null;

        var categories = SelectedCategories();
        _allParameters.Clear();
        _selectedParams.Clear();

        if (categories.Count > 0)
        {
            try
            {
                var parameters = _source.DiscoverParameters(
                    categories.Select(c => c.OstCode).ToList(),
                    IncludeTypeParametersBox.IsChecked == true);

                foreach (var info in parameters)
                    _allParameters.Add(new ParameterRow(info, T("pbi.badge.type"), DescribeParameter(info)));

                foreach (var key in keep)
                {
                    var row = _allParameters.FirstOrDefault(p => p.Key == key);
                    if (row != null && !_selectedParams.Contains(row)) _selectedParams.Add(row);
                }
            }
            catch (Exception ex)
            {
                DebugLog($"DiscoverParameters failed: {ex}");
                SetStatus(T("pbi.status.parameters_failed", ex.Message), attention: true);
            }
        }

        NormalizeSelectedOrder();
        ApplyParameterFilter();
    }

    /// <summary>The tooltip of a parameter row: what the row itself has no room for.</summary>
    private static string DescribeParameter(ParameterInfo info)
    {
        var parts = new List<string>
        {
            info.Scope == "Type" ? T("pbi.param.type") : T("pbi.param.instance"),
            T("pbi.param.coverage", info.CoveragePercent),
        };
        if (info.IsReadOnly) parts.Add(T("pbi.param.read_only"));
        if (info.IsShared) parts.Add(T("pbi.param.shared"));
        return info.Name + Environment.NewLine + info.GroupName + Environment.NewLine + string.Join(" · ", parts);
    }

    private void ParameterFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyParameterFilter();
    }

    private void HideEmpty_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ApplyParameterFilter();
    }

    private void IncludeTypeParameters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _bulk) return;
        if (_mode == SourceMode.Categories) LoadParametersNow();
    }

    /// <summary>Fills the "available" list: everything not chosen yet that passes the filters.</summary>
    private void ApplyParameterFilter()
    {
        string query = ParameterFilter.Text.Trim();
        bool hideEmpty = HideEmptyBox.IsChecked == true;
        ParameterFilterPlaceholder.Visibility = ParameterFilter.Text.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;

        var chosen = new HashSet<string>(_selectedParams.Select(p => p.Key));
        IEnumerable<ParameterRow> rows = _allParameters.Where(p => !chosen.Contains(p.Key));
        if (hideEmpty) rows = rows.Where(p => p.CoveragePercent > 0);
        if (query.Length > 0)
        {
            rows = rows.Where(p =>
                p.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                p.GroupName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        _availableParams.Clear();
        foreach (var p in rows) _availableParams.Add(p);

        if (_availableParams.Count > 0)
        {
            AvailableEmpty.Visibility = Visibility.Collapsed;
        }
        else
        {
            AvailableEmpty.Visibility = Visibility.Visible;
            AvailableEmpty.Text = SelectedCategories().Count == 0 ? T("pbi.available.pick_category")
                : _allParameters.Count == _selectedParams.Count ? T("pbi.available.all_added")
                : T("pbi.available.no_match");
        }

        RefreshCounts();
    }

    // ───────────────────────── Moving parameters into columns ─────────────────────────

    private void MoveToSelected_Click(object sender, RoutedEventArgs e)
        => AddColumns(AvailableList.SelectedItems.Cast<ParameterRow>().ToList());

    private void MoveAllToSelected_Click(object sender, RoutedEventArgs e)
        => AddColumns(_availableParams.ToList());

    private void MoveToAvailable_Click(object sender, RoutedEventArgs e)
        => RemoveColumns(SelectedList.SelectedItems.Cast<ParameterRow>().ToList());

    private void MoveAllToAvailable_Click(object sender, RoutedEventArgs e)
        => RemoveColumns(_selectedParams.ToList());

    private void AvailableParams_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowUnder(AvailableList, e) is ParameterRow row) AddColumns(new List<ParameterRow> { row });
    }

    private void SelectedParams_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowUnder(SelectedList, e) is ParameterRow row) RemoveColumns(new List<ParameterRow> { row });
    }

    /// <summary>The row that was double-clicked; null on the scroll bar or on empty space.</summary>
    private static object? RowUnder(ListBox list, MouseButtonEventArgs e)
    {
        if (!(e.OriginalSource is DependencyObject source)) return null;
        var item = ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
        return item?.DataContext;
    }

    private void AddColumns(List<ParameterRow> rows)
    {
        if (rows.Count == 0) return;
        foreach (var row in rows)
            if (!_selectedParams.Contains(row)) _selectedParams.Add(row);

        ClearStatus();
        NormalizeSelectedOrder();
        ApplyParameterFilter();
    }

    private void RemoveColumns(List<ParameterRow> rows)
    {
        if (rows.Count == 0) return;
        foreach (var row in rows) _selectedParams.Remove(row);

        ClearStatus();
        NormalizeSelectedOrder();
        ApplyParameterFilter();
    }

    private void MoveSelectedUp_Click(object sender, RoutedEventArgs e) => MoveColumn(-1);

    private void MoveSelectedDown_Click(object sender, RoutedEventArgs e) => MoveColumn(+1);

    private void MoveColumn(int offset)
    {
        if (!(SelectedList.SelectedItem is ParameterRow row)) return;
        int from = _selectedParams.IndexOf(row);
        int to = from + offset;
        if (from < 0 || to < 0 || to >= _selectedParams.Count) return;
        // The CSV lists instance parameters first and type parameters after
        // them: a column moves within its own group only.
        if (_selectedParams[to].IsType != row.IsType) return;

        _selectedParams.Move(from, to);
        NormalizeSelectedOrder();
        SelectedList.SelectedItem = row;
        SelectedList.ScrollIntoView(row);
    }

    /// <summary>
    /// Keeps the list in the order the columns have in the file: instance
    /// parameters in the user's order, then type parameters in the user's
    /// order. Renumbers the rows.
    /// </summary>
    private void NormalizeSelectedOrder()
    {
        var ordered = _selectedParams.Where(p => !p.IsType)
            .Concat(_selectedParams.Where(p => p.IsType))
            .ToList();

        if (!ordered.SequenceEqual(_selectedParams))
        {
            _selectedParams.Clear();
            foreach (var p in ordered) _selectedParams.Add(p);
        }

        for (int i = 0; i < _selectedParams.Count; i++)
            _selectedParams[i].OrderIndex = i + 1;
    }

    // ───────────────────────── Counts and footer ─────────────────────────

    private void RefreshCounts()
    {
        int categories = SelectedCategories().Count;
        int schedules = SelectedSchedules().Count;
        int columns = _selectedParams.Count;

        CategoriesCount.Text = Counted("pbi.count.chosen_f", categories);
        SchedulesCount.Text = Counted("pbi.count.schedules_chosen", schedules);

        SelectedCaption.Text = columns == 0
            ? T("pbi.columns.caption_none")
            : Counted("pbi.columns.caption", columns);
        SelectedEmpty.Visibility = columns == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateFooter();
    }

    /// <summary>What is chosen so far, in one line.</summary>
    private string Summary()
    {
        if (_mode == SourceMode.Schedules)
            return Counted("pbi.count.schedules", SelectedSchedules().Count);

        return Counted("pbi.count.categories", SelectedCategories().Count) + " · "
             + Counted("pbi.count.columns", _selectedParams.Count);
    }

    private void UpdateFooter()
    {
        bool hasStatus = !string.IsNullOrEmpty(_status);
        FooterText.Text = hasStatus ? _status : Summary();
        FooterText.ToolTip = hasStatus ? _status : null;
        FooterText.Foreground = (Brush)FindResource(
            hasStatus && _statusNeedsAttention ? "Vx.Caution"
            : hasStatus ? "Vx.InkSoft"
            : "Vx.Muted");
    }

    private void ClearStatus()
    {
        _status = null;
        _statusNeedsAttention = false;
        UpdateFooter();
    }

    // SetStatus can be called after an await whose continuation ran on a
    // thread-pool thread (the HTTP calls behind the Power BI refresh).
    // Touching the window off the UI thread throws, so go back through the
    // Dispatcher.
    private void SetStatus(string text, bool attention = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(text, attention));
            return;
        }

        _status = text;
        _statusNeedsAttention = attention;
        UpdateFooter();
    }

    // ───────────────────────── Profiles ─────────────────────────

    private void ProfilesBtn_Click(object sender, RoutedEventArgs e)
    {
        // Line the menu's right edge up with the button's.
        if (ProfilesPopup.Child is FrameworkElement menu && !double.IsNaN(menu.Width))
            ProfilesPopup.HorizontalOffset = ProfilesBtn.ActualWidth - menu.Width;
        ProfilesPopup.IsOpen = true;
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        ProfilesPopup.IsOpen = false;

        var profiles = ProfileStore.LoadAll();
        if (profiles.Count == 0)
        {
            SetStatus(T("pbi.status.no_profiles"), attention: true);
            return;
        }

        var dialog = new ProfilePickerDialog(profiles) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedProfile == null) return;
        ApplyProfile(dialog.SelectedProfile);
    }

    /// <summary>
    /// Puts a saved profile on the window: source, categories, columns (in
    /// their saved order), output and column types.
    /// </summary>
    public void ApplyProfile(PowerBiExportProfile profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        LoadFromSource();

        // A hand-edited or imported profile may leave a list out.
        var categories = new HashSet<string>(profile.Categories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var schedules = new HashSet<long>(profile.ScheduleIds ?? new List<long>());

        _bulk = true;
        try
        {
            foreach (var c in _allCategories) c.IsSelected = categories.Contains(c.OstCode);
            foreach (var s in _schedules) s.IsSelected = schedules.Contains(s.ScheduleId);

            if (profile.UseSchedules) ModeSchedulesRadio.IsChecked = true;
            else if (profile.ScopeMode == "ActiveView") ScopeViewRadio.IsChecked = true;
            else if (profile.ScopeMode == "Selection") ScopeSelectionRadio.IsChecked = true;
            else ScopeWholeRadio.IsChecked = true;

            IncludeTypeParametersBox.IsChecked = profile.IncludeTypeParameters;
        }
        finally
        {
            _bulk = false;
        }

        if (!string.IsNullOrEmpty(profile.OutputFolder)) OutputFolderBox.Text = profile.OutputFolder;
        if (!string.IsNullOrEmpty(profile.FileName)) FileNameBox.Text = profile.FileName;
        OverwriteBox.IsChecked = profile.OverwriteFile;
        AutoExportBox.IsChecked = profile.AutoExportOnSave;
        TriggerRefreshBox.IsChecked = profile.TriggerPbiRefresh;
        RefreshWorkspaceIdBox.Text = profile.RefreshWorkspaceId ?? "";
        RefreshDatasetIdBox.Text = profile.RefreshDatasetId ?? "";

        // The profile's columns are picked up when the parameters load.
        _pendingColumns = (profile.InstanceParameters ?? new List<string>()).Select(n => ParameterRow.MakeKey("Instance", n))
            .Concat((profile.TypeParameters ?? new List<string>()).Select(n => ParameterRow.MakeKey("Type", n)))
            .ToList();
        ApplySource();
        if (_mode == SourceMode.Schedules)
        {
            _pendingColumns = null;
            RefreshScheduleFields(SelectedSchedules().FirstOrDefault());
        }
        RefreshCategoryTab();
        RefreshCounts();

        // Column types last, once the columns are there: the saved types
        // first, then the mode ("Suggested" fills in a list saved empty).
        _columnTypes.Clear();
        foreach (var ct in profile.ColumnTypes ?? new List<ColumnTypeMapping>())
            _columnTypes.Add(new ColumnTypeMapping { ColumnName = ct.ColumnName, PbiType = ct.PbiType, Format = ct.Format });
        _bulk = true;
        try
        {
            switch ((profile.SchemaMappingMode ?? "Auto").ToLowerInvariant())
            {
                case "suggested": SchemaModeSuggestedRadio.IsChecked = true; break;
                case "custom": SchemaModeCustomRadio.IsChecked = true; break;
                default: SchemaModeAutoRadio.IsChecked = true; break;
            }
        }
        finally
        {
            _bulk = false;
        }
        ApplySchemaMode();

        // On step 2 already: show the output of what was just loaded, or go
        // back to step 1 when the profile has nothing this model can export.
        if (_step == 2 && !GoToOutput()) ShowStep(1);
        if (string.IsNullOrEmpty(_status)) SetStatus(T("pbi.status.profile_loaded", profile.Name));
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        ProfilesPopup.IsOpen = false;

        var dialog = new ProfileNameDialog { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ProfileName)) return;

        var profile = BuildCurrentProfile(dialog.ProfileName);
        try
        {
            ProfileStore.Save(profile);
            SetStatus(T("pbi.status.profile_saved", profile.Name, ProfileStore.GetProfilesDirectory()));
        }
        catch (Exception ex)
        {
            SetStatus(T("pbi.status.profile_save_failed", ex.Message), attention: true);
        }
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        ProfilesPopup.IsOpen = false;

        var dialog = new OpenFileDialog
        {
            Title = T("pbi.profiles.import_title"),
            Filter = T("pbi.profiles.import_filter") + " (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var profile = Newtonsoft.Json.JsonConvert.DeserializeObject<PowerBiExportProfile>(json);
            if (profile == null || string.IsNullOrWhiteSpace(profile.Name))
            {
                SetStatus(T("pbi.status.profile_invalid"), attention: true);
                return;
            }
            ProfileStore.Save(profile);
            ApplyProfile(profile);
            SetStatus(T("pbi.status.profile_imported", profile.Name));
        }
        catch (Exception ex)
        {
            SetStatus(T("pbi.status.profile_import_failed", ex.Message), attention: true);
        }
    }

    private void OpenProfilesFolder_Click(object sender, RoutedEventArgs e)
    {
        ProfilesPopup.IsOpen = false;
        try
        {
            var dir = ProfileStore.GetProfilesDirectory();
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch (Exception ex)
        {
            SetStatus(T("pbi.status.open_folder_failed", ex.Message), attention: true);
        }
    }

    private PowerBiExportProfile BuildCurrentProfile(string name)
    {
        bool schedules = _mode == SourceMode.Schedules;
        return new PowerBiExportProfile
        {
            Name = name,
            UseSchedules = schedules,
            Categories = SelectedCategories().Select(c => c.OstCode).ToList(),
            ScheduleIds = SelectedSchedules().Select(s => s.ScheduleId).ToList(),
            // A schedule export has its own columns and file names.
            InstanceParameters = schedules
                ? new List<string>()
                : _selectedParams.Where(p => !p.IsType).Select(p => p.Name).ToList(),
            TypeParameters = schedules
                ? new List<string>()
                : _selectedParams.Where(p => p.IsType).Select(p => p.Name).ToList(),
            IncludeTypeParameters = IncludeTypeParametersBox.IsChecked == true,
            OutputFolder = OutputFolderBox.Text,
            FileName = schedules ? "" : FileNameBox.Text,
            OverwriteFile = OverwriteBox.IsChecked == true,
            AutoExportOnSave = AutoExportBox.IsChecked == true,
            TriggerPbiRefresh = TriggerRefreshBox.IsChecked == true,
            RefreshWorkspaceId = string.IsNullOrWhiteSpace(RefreshWorkspaceIdBox.Text) ? null : RefreshWorkspaceIdBox.Text.Trim(),
            RefreshDatasetId = string.IsNullOrWhiteSpace(RefreshDatasetIdBox.Text) ? null : RefreshDatasetIdBox.Text.Trim(),
            ScopeMode = _scope.ToString(),
            SchemaMappingMode = SchemaModeWireValue(),
            ColumnTypes = _columnTypes.Select(c => new ColumnTypeMapping
            {
                ColumnName = c.ColumnName,
                PbiType = c.PbiType,
                Format = c.Format
            }).ToList()
        };
    }

    // ───────────────────────── Step 2: file ─────────────────────────

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        // A file dialog used as a folder picker (WPF has none on .NET
        // Framework): the user opens the folder and presses "Open".
        var dialog = new OpenFileDialog
        {
            Title = T("pbi.browse_title"),
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = T("pbi.browse_placeholder"),
            Filter = T("pbi.browse_filter") + "|*.this-directory"
        };
        if (!string.IsNullOrEmpty(OutputFolderBox.Text) && Directory.Exists(OutputFolderBox.Text))
            dialog.InitialDirectory = OutputFolderBox.Text;

        if (dialog.ShowDialog() == true)
        {
            var folder = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(folder)) OutputFolderBox.Text = folder!;
            RefreshPreview();
        }
    }

    /// <summary>
    /// Opens the output folder in Explorer, to see what a previous export
    /// wrote. The folder is created when it does not exist yet.
    /// </summary>
    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = OutputFolderBox.Text.Trim();
            if (dir.Length == 0)
            {
                SetStatus(T("pbi.status.need_folder"), attention: true);
                return;
            }
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch (Exception ex)
        {
            SetStatus(T("pbi.status.open_folder_failed", ex.Message), attention: true);
        }
    }

    private void PopulateScheduleFilesList()
    {
        var files = SelectedSchedules()
            .Select(s => $"schedule_{SanitizeFileName(s.Name)}.csv")
            .ToList();
        ScheduleFilesList.ItemsSource = files;
        ScheduleFilesLabel.Text = Counted("pbi.schedule_files", files.Count);
    }

    private string SuggestFileName()
    {
        var title = Path.GetFileNameWithoutExtension(_source.DocumentTitle ?? "");
        return string.IsNullOrWhiteSpace(title) ? "elements.csv" : SanitizeFileName(title) + ".csv";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }

    private void TriggerRefreshBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        RefreshConfigPanel.Visibility = TriggerRefreshBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ───────────────────────── Step 2: preview ─────────────────────────

    private void RefreshPreview()
    {
        try
        {
            if (_mode == SourceMode.Schedules) RefreshSchedulePreview();
            else RefreshCategoryPreview();
        }
        catch (Exception ex)
        {
            DebugLog($"Preview failed: {ex}");
            ShowPreview(null, null);
            SummaryText.Text = "";
            SummaryNote.Text = "";
            SummaryWarning.Visibility = Visibility.Collapsed;
            SetStatus(T("pbi.status.preview_failed", ex.Message), attention: true);
        }
    }

    private void RefreshCategoryPreview()
    {
        // A parameter named like a built-in column ("Family", "Type"...) is
        // left out, here and in the CSV: the built-in column already carries
        // that value.
        var builtIns = BuiltInColumnNames();
        var instance = new List<string>();
        var type = new List<string>();
        int skipped = 0;
        foreach (var p in _selectedParams)
        {
            if (string.IsNullOrEmpty(p.Name)) continue;
            if (builtIns.Contains(p.Name)) { skipped++; continue; }
            if (p.IsType) type.Add(p.Name);
            else instance.Add(p.Name);
        }

        var categories = SelectedCategories();
        var preview = _source.PreviewElements(_scope, categories.Select(c => c.OstCode).ToList(),
            instance, type, PreviewRowCount);

        var headers = new List<string> { "ElementId", "Category", "Family", "Type" };
        headers.AddRange(instance);
        headers.AddRange(type.Select(n => "[Type] " + n));
        ShowPreview(headers, preview.Rows);

        PreviewCaption.Text = T("pbi.preview.first_rows", PreviewRowCount);
        int columns = builtIns.Count + instance.Count + type.Count;
        SummaryText.Text = T("pbi.preview.summary", preview.TotalRows.ToString("N0"), columns, ScopeLabel());
        SummaryNote.Text = skipped > 0
            ? T("pbi.preview.more_columns") + " " + Counted("pbi.preview.skipped", skipped)
            : T("pbi.preview.more_columns");

        // The tool stops at the profile's row limit.
        int limit = new PowerBiExportProfile().MaxElements;
        bool capped = preview.TotalRows > limit;
        SummaryWarning.Visibility = capped ? Visibility.Visible : Visibility.Collapsed;
        if (capped) SummaryWarning.Text = T("pbi.preview.capped", limit.ToString("N0"));
    }

    private void RefreshSchedulePreview()
    {
        SummaryWarning.Visibility = Visibility.Collapsed;
        SummaryNote.Text = "";

        var selected = SelectedSchedules();
        if (selected.Count == 0)
        {
            ShowPreview(null, null);
            PreviewCaption.Text = "";
            SummaryText.Text = "";
            return;
        }

        var first = selected[0];
        var preview = _source.PreviewSchedule(first.ScheduleId, PreviewRowCount);
        ShowPreview(preview.Headers, preview.Rows);

        PreviewCaption.Text = T("pbi.preview.first_rows_of", PreviewRowCount, first.Name);
        SummaryText.Text = Counted("pbi.count.schedules", selected.Count) + " · "
            + Counted("pbi.rows", selected.Sum(s => s.RowCount));
        if (selected.Count > 1) SummaryNote.Text = T("pbi.preview.first_schedule_only");
    }

    private string ScopeLabel()
    {
        switch (_scope)
        {
            case PowerBiScope.ActiveView: return T("pbi.scope.view").ToLower();
            case PowerBiScope.Selection: return T("pbi.scope.selection").ToLower();
            default: return T("pbi.scope.whole").ToLower();
        }
    }

    /// <summary>
    /// Draws the preview table. A plain grid of text cells: the column names
    /// come from the model (they may hold dots or brackets, which a bound
    /// DataGrid column would read as a path) and there are only a few rows.
    /// </summary>
    private void ShowPreview(IList<string>? headers, IList<string[]>? rows)
    {
        PreviewHost.Children.Clear();
        PreviewHost.ColumnDefinitions.Clear();
        PreviewHost.RowDefinitions.Clear();

        if (headers == null || headers.Count == 0)
        {
            PreviewEmpty.Text = T("pbi.preview.nothing");
            PreviewEmpty.Visibility = Visibility.Visible;
            return;
        }

        int rowCount = rows?.Count ?? 0;
        PreviewEmpty.Text = T("pbi.preview.no_rows");
        PreviewEmpty.Visibility = rowCount == 0 ? Visibility.Visible : Visibility.Collapsed;

        var hairline = (Brush)FindResource("Vx.Hairline");
        var rowLine = (Brush)FindResource("Vx.Raised");
        var muted = (Brush)FindResource("Vx.Muted");

        // One column per header, then one that takes the rest of the width so
        // the lines run to the edge of the card.
        for (int c = 0; c < headers.Count; c++)
            PreviewHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        PreviewHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r <= rowCount; r++)
            PreviewHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int c = 0; c <= headers.Count; c++)
        {
            bool filler = c == headers.Count;
            var padding = new Thickness(c == 0 ? 16 : 12, 0, c == headers.Count - 1 ? 16 : 0, 0);

            var headerCell = new Border
            {
                Height = 30,
                Padding = padding,
                BorderBrush = hairline,
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            if (!filler)
            {
                headerCell.Child = new TextBlock
                {
                    Text = headers[c],
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = muted,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            Grid.SetColumn(headerCell, c);
            Grid.SetRow(headerCell, 0);
            PreviewHost.Children.Add(headerCell);

            for (int r = 0; r < rowCount; r++)
            {
                var cell = new Border
                {
                    Height = 34,
                    Padding = padding,
                    BorderBrush = rowLine,
                    BorderThickness = new Thickness(0, 0, 0, r == rowCount - 1 ? 0 : 1),
                };
                if (!filler)
                {
                    var row = rows![r];
                    string text = c < row.Length ? row[c] ?? "" : "";
                    cell.Child = new TextBlock
                    {
                        Text = text,
                        FontSize = 12.5,
                        MaxWidth = 200,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = text.Length > 0 ? text : null,
                    };
                }
                Grid.SetColumn(cell, c);
                Grid.SetRow(cell, r + 1);
                PreviewHost.Children.Add(cell);
            }
        }
    }

    // ───────────────────────── Step 2: column types (advanced) ─────────────────────────

    private void AdvancedToggle_Click(object sender, RoutedEventArgs e) => SetAdvancedOpen(!_advancedOpen);

    /// <summary>Opens the column types panel. Used by the UI preview renderer.</summary>
    public void ExpandAdvanced() => SetAdvancedOpen(true);

    /// <summary>
    /// The column types take the preview's place while they are open: the
    /// list needs the height, and the two are not read together.
    /// </summary>
    private void SetAdvancedOpen(bool open)
    {
        _advancedOpen = open;
        AdvancedBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PreviewCard.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        PreviewRow.Height = open ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        AdvancedRow.Height = open ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        AdvancedCard.Margin = new Thickness(0, open ? 0 : 12, 0, 0);
        AdvancedChevron.Data = (Geometry)FindResource(open ? "Vx.Icon.ChevronDown" : "Vx.Icon.ChevronRight");
    }

    private void SchemaMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _bulk) return;
        ApplySchemaMode();
    }

    private void ApplySchemaMode()
    {
        if (SchemaModeSuggestedRadio.IsChecked == true) _schemaMode = SchemaMode.Suggested;
        else if (SchemaModeCustomRadio.IsChecked == true) _schemaMode = SchemaMode.Custom;
        else _schemaMode = SchemaMode.Auto;

        // Auto: nothing to edit. Suggested and Custom open the list.
        ColumnTypesList.IsEnabled = _schemaMode != SchemaMode.Auto;
        AdvancedModeText.Text = _schemaMode == SchemaMode.Suggested ? T("pbi.schema.suggested")
            : _schemaMode == SchemaMode.Custom ? T("pbi.schema.custom")
            : T("pbi.schema.auto");

        if (_schemaMode == SchemaMode.Suggested && _columnTypes.Count == 0)
            ApplySuggestedTypes();
    }

    private void SuggestTypes_Click(object sender, RoutedEventArgs e)
    {
        // Switches to Suggested and rebuilds from the current columns: this
        // replaces what was typed under Custom, which is what the button says.
        SchemaModeSuggestedRadio.IsChecked = true;
        ApplySuggestedTypes();
    }

    /// <summary>
    /// Rebuilds the column types from the chosen columns: built-in columns
    /// are typed by convention, parameters by their name and group.
    /// </summary>
    private void ApplySuggestedTypes()
    {
        _columnTypes.Clear();
        if (_mode != SourceMode.Categories) return;

        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "ElementId", PbiType = "int" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "UniqueId", PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "Category", PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "Family", PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "Type", PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "DocumentTitle", PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "DocumentPath", PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "EpisodeId", PbiType = "text" });

        // A parameter named like a built-in column is not written (the tool
        // filters it out), so it gets no entry either.
        var builtIns = BuiltInColumnNames();
        foreach (var p in _selectedParams)
        {
            if (string.IsNullOrEmpty(p.Name) || builtIns.Contains(p.Name)) continue;
            _columnTypes.Add(new ColumnTypeMapping
            {
                ColumnName = p.IsType ? $"[Type] {p.Name}" : p.Name,
                PbiType = InferPbiTypeForParam(p.Name, p.GroupName)
            });
        }
    }

    /// <summary>
    /// The columns the CSV always has. One list for the preview and for the
    /// column types, so they agree. Mirrors the header written by
    /// <c>PushToPowerBiTool</c> (schema v2).
    /// </summary>
    private static HashSet<string> BuiltInColumnNames() => new HashSet<string>(
        new[] { "ElementId", "UniqueId", "Category", "Family", "Type", "DocumentTitle", "DocumentPath", "EpisodeId" },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>The headers of the CSV for the current selection, in order.</summary>
    private List<string> ComputeCurrentColumnNames()
    {
        var columns = new List<string>
        {
            "ElementId", "UniqueId", "Category", "Family", "Type", "DocumentTitle", "DocumentPath", "EpisodeId"
        };
        if (_mode != SourceMode.Categories) return columns;

        var builtIns = BuiltInColumnNames();
        foreach (var p in _selectedParams)
        {
            if (string.IsNullOrEmpty(p.Name) || builtIns.Contains(p.Name)) continue;
            columns.Add(p.IsType ? $"[Type] {p.Name}" : p.Name);
        }
        return columns;
    }

    /// <summary>
    /// Brings the column types in line with the columns chosen, keeping the
    /// type already set (by the user or by "Suggest") on every column that is
    /// still there. New columns start as <c>auto</c>.
    /// </summary>
    private void SyncColumnTypesWithSelection()
    {
        var existing = new Dictionary<string, ColumnTypeMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _columnTypes) existing[c.ColumnName] = c;

        _columnTypes.Clear();
        foreach (var column in ComputeCurrentColumnNames())
        {
            _columnTypes.Add(existing.TryGetValue(column, out var previous)
                ? previous
                : new ColumnTypeMapping { ColumnName = column, PbiType = "auto" });
        }
    }

    /// <summary>
    /// Parameter name and group to a likely Power BI type. Conservative:
    /// <c>text</c> when nothing matches, so a wrong guess does not break a
    /// refresh.
    /// </summary>
    private static string InferPbiTypeForParam(string name, string? groupName)
    {
        var n = name?.ToLowerInvariant() ?? "";
        var g = groupName?.ToLowerInvariant() ?? "";

        // Ids
        if (n.EndsWith(" id") || n.EndsWith("_id") || n == "id") return "int";

        // Yes / no
        if (n.StartsWith("is ") || n.StartsWith("has ") || n.StartsWith("can ")) return "bool";

        // Money
        if (n.Contains("cost") || n.Contains("price") || n.Contains("importo")
            || n.Contains("total") || n.Contains("subtotal") || n.Contains("amount"))
            return "fixed";

        // Percentages
        if (n.Contains("percent") || n.Contains("%") || g.Contains("percent"))
            return "percent";

        // Dates: only the obvious patterns
        if (n.EndsWith(" date") || n.EndsWith("_date") || n.Contains("data") && (n.Contains("creazione") || n.Contains("modifica")))
            return "date";

        // Dimensions and quantities (English, Italian and Spanish names)
        if (g.Contains("dimension") || g.Contains("constraint") || g.Contains("graphic")
            || n.Contains("length") || n.Contains("lunghezza") || n.Contains("longitud")
            || n.Contains("area") || n.Contains("área") || n.Contains("volume")
            || n.Contains("width") || n.Contains("height") || n.Contains("depth")
            || n.Contains("altezza") || n.Contains("larghezza") || n.Contains("profondità")
            || n.Contains("anchura") || n.Contains("altura") || n.Contains("profundidad")
            || n.Contains("angle") || n.Contains("angolo") || n.Contains("ángulo")
            || n.Contains("count") || n.Contains("number") || n.Contains("numero") || n.Contains("número"))
            return "number";

        return "text";
    }

    /// <summary>The mode as the tool expects it.</summary>
    private string SchemaModeWireValue()
    {
        switch (_schemaMode)
        {
            case SchemaMode.Suggested: return "Suggested";
            case SchemaMode.Custom: return "Custom";
            default: return "Auto";
        }
    }

    // ───────────────────────── Export ─────────────────────────

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var profile = BuildCurrentProfile("_lastExport");

        if (_mode == SourceMode.Categories && profile.Categories.Count == 0)
        {
            SetStatus(T("pbi.need_category"), attention: true);
            return;
        }
        if (_mode == SourceMode.Schedules && profile.ScheduleIds.Count == 0)
        {
            SetStatus(T("pbi.need_schedule"), attention: true);
            return;
        }

        SetStatus(T("pbi.status.exporting"));
        ExportBtn.IsEnabled = false;
        BackBtn.IsEnabled = false;

        try
        {
            ProfileStore.Save(profile);

            var outcome = _source.Export(profile, _scope);
            if (outcome.Kind == PowerBiExportResultKind.RouterUnavailable)
            {
                SetStatus(T("pbi.status.no_router"), attention: true);
                return;
            }

            if (RegisterProtocolBox.IsChecked == true)
            {
                try { ProtocolHandlerRegistrar.Register(); }
                catch (Exception ex) { DebugLog($"Protocol handler registration failed: {ex.Message}"); }
            }
            try { _source.SetAutoExport(AutoExportBox.IsChecked == true ? profile : null); }
            catch (Exception ex) { DebugLog($"Auto-export hook failed: {ex.Message}"); }

            if (outcome.Kind == PowerBiExportResultKind.NoResponse)
            {
                SetStatus(T("pbi.status.no_response"), attention: true);
                return;
            }
            if (outcome.Kind == PowerBiExportResultKind.Failed)
            {
                var code = outcome.ErrorCode ?? "Unknown";
                var message = outcome.ErrorMessage ?? T("pbi.unknown_error");
                SetStatus(T("pbi.status.export_failed", code, message), attention: true);
                _source.ShowError(T("pbi.dialog.export_failed", code), message, null);
                return;
            }

            SetStatus(T("pbi.status.exported", outcome.RowCount ?? "?", outcome.Path ?? T("pbi.unknown_path")));
            ShowExportDone(outcome);

            // Not awaited: the refresh must not hold Revit's main thread.
            if (profile.TriggerPbiRefresh
                && !string.IsNullOrWhiteSpace(profile.RefreshWorkspaceId)
                && !string.IsNullOrWhiteSpace(profile.RefreshDatasetId))
            {
                _ = FirePbiRefreshAsync(profile.RefreshWorkspaceId!, profile.RefreshDatasetId!);
            }
        }
        catch (Exception ex)
        {
            DebugLog($"Export failed: {ex}");
            SetStatus(T("pbi.status.unexpected", ex.GetType().Name, ex.Message), attention: true);
            _source.ShowError(T("pbi.dialog.unexpected"), $"{ex.GetType().Name}: {ex.Message}", ex.StackTrace);
        }
        finally
        {
            ExportBtn.IsEnabled = true;
            BackBtn.IsEnabled = true;
        }
    }

    /// <summary>
    /// Tells the user what was written and offers to open the folder. The
    /// path is a CSV, or the folder when several schedules were exported.
    /// </summary>
    private void ShowExportDone(PowerBiExportOutcome outcome)
    {
        try
        {
            string folder = "";
            if (!string.IsNullOrEmpty(outcome.Path))
            {
                folder = Directory.Exists(outcome.Path)
                    ? outcome.Path!
                    : Path.GetDirectoryName(outcome.Path) ?? "";
            }
            if (string.IsNullOrEmpty(folder)) folder = OutputFolderBox.Text;

            string instruction, detail;
            if (_mode == SourceMode.Schedules)
            {
                instruction = Counted("pbi.dialog.exported_schedules", SelectedSchedules().Count);
                detail = T("pbi.dialog.files_in", folder);
            }
            else
            {
                instruction = T("pbi.dialog.exported_rows", outcome.RowCount ?? "?");
                detail = T("pbi.dialog.file", outcome.Path ?? "");
            }

            bool openFolder = _source.ShowExportDone(T("pbi.dialog.done_title"), instruction, detail,
                T("pbi.dialog.open_folder"), T("pbi.dialog.open_folder_hint"));
            if (openFolder && !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch (Exception ex)
        {
            DebugLog($"Export-done dialog failed (not fatal): {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task FirePbiRefreshAsync(string workspaceId, string datasetId)
    {
        try
        {
            SetStatus(T("pbi.status.refresh_starting"));
            var settings = RevitCortex.Plugin.PowerBiLive.PowerBiSettings.Load();
            var auth = new RevitCortex.Plugin.PowerBiLive.PowerBiAuthService(settings);
            var authState = await auth.TryAcquireSilentAsync();
            if (!authState.IsSignedIn || string.IsNullOrEmpty(authState.AccessToken))
            {
                SetStatus(T("pbi.status.refresh_not_signed_in"), attention: true);
                return;
            }

            using var client = new RevitCortex.Plugin.PowerBiLive.PowerBiServiceClient(authState.AccessToken!);
            var requestId = await client.TriggerRefreshAsync(workspaceId, datasetId);
            SetStatus(string.IsNullOrEmpty(requestId)
                ? T("pbi.status.refresh_queued")
                : T("pbi.status.refresh_started", requestId));
        }
        catch (Exception ex)
        {
            SetStatus(T("pbi.status.refresh_failed", ex.Message), attention: true);
            DebugLog($"Power BI refresh failed: {ex}");
        }
    }

    // ───────────────────────── Helpers ─────────────────────────

    private void Card_SizeChanged(object sender, SizeChangedEventArgs e) => ClipToCorners(sender, 12);

    private void Well_SizeChanged(object sender, SizeChangedEventArgs e) => ClipToCorners(sender, 10);

    /// <summary>Rows are square; clip them to the rounded corners of their card.</summary>
    private static void ClipToCorners(object sender, double radius)
    {
        if (sender is Border border)
        {
            border.Clip = new RectangleGeometry(
                new Rect(0, 0, border.ActualWidth, border.ActualHeight), radius, radius);
        }
    }

    private static string SuggestDefaultOutputFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
        {
            Path.Combine(userProfile, "OneDrive - GPA Ingegneria Srl"),
            Path.Combine(userProfile, "OneDrive - GPA Partners"),
            Path.Combine(userProfile, "OneDrive")
        })
        {
            if (Directory.Exists(candidate))
                return Path.Combine(candidate, "RevitCortex");
        }
        return Path.Combine(userProfile, "Documents", "RevitCortex");
    }

    private static readonly string DebugLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "powerbi_debug.log");

    private static void DebugLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(DebugLogPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { /* never throw from the logger */ }
    }

    // ───────────────────────── Rows ─────────────────────────

    // These must be public: WPF binding reads the properties by reflection
    // and silently shows nothing for a private nested type.

    public abstract class ViewRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                Raise(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Raise(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class CategoryRow : ViewRow
    {
        public string DisplayName { get; }
        public string OstCode { get; }
        public int InstanceCount { get; }
        public string CountText { get; }
        public string CategoryType { get; }

        /// <summary>Has elements in the active view / the selection (always true for the whole model).</summary>
        public bool InScope { get; set; } = true;

        public CategoryRow(CategoryInfo info)
        {
            DisplayName = info.DisplayName;
            OstCode = info.OstCode;
            InstanceCount = info.InstanceCount;
            CountText = info.InstanceCount.ToString("N0");
            CategoryType = info.CategoryType;
        }
    }

    public class ScheduleRow : ViewRow
    {
        public long ScheduleId { get; }
        public string Name { get; }
        public string CategoryName { get; }
        public int RowCount { get; }
        public string RowsText { get; }

        public ScheduleRow(ScheduleInfo info, string rowsText)
        {
            ScheduleId = info.ScheduleId;
            Name = info.Name;
            CategoryName = info.CategoryName;
            RowCount = info.RowCount;
            RowsText = rowsText;
        }
    }

    public class ParameterRow : ViewRow
    {
        private int _orderIndex;

        public string Name { get; }
        public string Scope { get; }
        public bool IsType { get; }
        public string GroupName { get; }
        public int CoveragePercent { get; }

        /// <summary>The word shown next to a type parameter.</summary>
        public string ScopeBadge { get; }
        public string Tooltip { get; }

        /// <summary>Scope and name: the same name can be an instance and a type parameter.</summary>
        public string Key { get; }

        /// <summary>Position among the chosen columns, from 1.</summary>
        public int OrderIndex
        {
            get => _orderIndex;
            set
            {
                if (_orderIndex == value) return;
                _orderIndex = value;
                Raise(nameof(OrderIndex));
            }
        }

        public ParameterRow(ParameterInfo info, string scopeBadge, string tooltip)
        {
            Name = info.Name;
            Scope = info.Scope;
            IsType = info.Scope == "Type";
            GroupName = info.GroupName;
            CoveragePercent = info.CoveragePercent;
            ScopeBadge = scopeBadge;
            Tooltip = tooltip;
            Key = MakeKey(info.Scope, info.Name);
        }

        public static string MakeKey(string scope, string name)
            => (scope == "Type" ? "Type" : "Instance") + "\n" + name;
    }

    public class ScheduleFieldRow
    {
        public string Header { get; }
        public string Badge { get; }
        public bool HasBadge { get; }

        public ScheduleFieldRow(string header, string badge)
        {
            Header = header;
            Badge = badge;
            HasBadge = badge.Length > 0;
        }
    }
}
