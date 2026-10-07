using System;
using System.Collections.Generic;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>Which elements of the model an export takes its rows from.</summary>
public enum PowerBiScope
{
    WholeModel,
    ActiveView,
    Selection
}

/// <summary>
/// Everything the export window needs from Revit, and nothing of Revit in its
/// signatures.
///
/// The window is a view: it never touches the Revit API or
/// <see cref="RevitCortexApp"/> itself, so it can be built where Revit is
/// absent (the UI preview tests hand it sample data through this interface).
/// Inside Revit the implementation is <see cref="RevitPowerBiExportSource"/>.
/// Every member runs on the Revit main thread, where the window lives.
/// </summary>
public interface IPowerBiExportSource
{
    /// <summary>Title of the document being exported; used to suggest a file name.</summary>
    string DocumentTitle { get; }

    /// <summary>Categories that have at least one element, sorted by name.</summary>
    List<CategoryInfo> DiscoverCategories();

    /// <summary>Schedules of the document, sorted by name.</summary>
    List<ScheduleInfo> DiscoverSchedules();

    /// <summary>
    /// Which of the given categories have elements in the active view or in
    /// the current selection.
    /// </summary>
    PowerBiScopeFilter ReadScope(PowerBiScope scope, IEnumerable<string> categoryOstCodes);

    /// <summary>Parameters found on a sample of elements of the given categories.</summary>
    List<ParameterInfo> DiscoverParameters(IEnumerable<string> categoryOstCodes, bool includeTypeParameters);

    /// <summary>Visible columns of a schedule, or null when it cannot be read.</summary>
    List<ScheduleFieldInfo>? GetScheduleFields(long scheduleId);

    /// <summary>
    /// The first <paramref name="take"/> elements as rows: ElementId, Category,
    /// Family, Type, then one cell per instance parameter and per type
    /// parameter, in the order given. <see cref="PowerBiPreview.TotalRows"/>
    /// is how many elements the export would write.
    /// </summary>
    PowerBiPreview PreviewElements(PowerBiScope scope, IList<string> categoryOstCodes,
        IList<string> instanceParameters, IList<string> typeParameters, int take);

    /// <summary>Headers and the first <paramref name="take"/> rows of a schedule.</summary>
    PowerBiPreview PreviewSchedule(long scheduleId, int take);

    /// <summary>Writes the CSV (or one per schedule) through the push_to_powerbi tool.</summary>
    PowerBiExportOutcome Export(PowerBiExportProfile profile, PowerBiScope scope);

    /// <summary>Re-exports with this profile on every save of the document; null turns it off.</summary>
    void SetAutoExport(PowerBiExportProfile? profile);

    /// <summary>Shows an error the user has to see (a Revit TaskDialog).</summary>
    void ShowError(string title, string message, string? details);

    /// <summary>
    /// Tells the user the export finished. True when they asked to open the
    /// output folder.
    /// </summary>
    bool ShowExportDone(string title, string instruction, string detail,
        string openFolderLabel, string openFolderHint);
}

/// <summary>Result of <see cref="IPowerBiExportSource.ReadScope"/>.</summary>
public class PowerBiScopeFilter
{
    /// <summary>The scope is "current selection" and nothing is selected in Revit.</summary>
    public bool SelectionEmpty { get; set; }

    /// <summary>OST codes of the categories that have elements in the scope.</summary>
    public HashSet<string> Categories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A visible column of a schedule.</summary>
public class ScheduleFieldInfo
{
    public string Header { get; set; } = "";

    /// <summary>"Instance" or "Type".</summary>
    public string Scope { get; set; } = "Instance";

    /// <summary>Calculated, count or formula column: it cannot be written back.</summary>
    public bool IsReadOnly { get; set; }
}

/// <summary>A few rows of what the export would write.</summary>
public class PowerBiPreview
{
    /// <summary>Column headers. Filled for schedules; element previews leave it to the caller.</summary>
    public List<string> Headers { get; set; } = new List<string>();

    public List<string[]> Rows { get; set; } = new List<string[]>();

    /// <summary>Rows the export would write (elements in scope, or schedule rows).</summary>
    public int TotalRows { get; set; }
}

public enum PowerBiExportResultKind
{
    Succeeded,

    /// <summary>The plugin's router is not there: Revit has to be restarted.</summary>
    RouterUnavailable,

    /// <summary>The tool returned nothing.</summary>
    NoResponse,

    /// <summary>The tool answered with an error: see ErrorCode and ErrorMessage.</summary>
    Failed
}

/// <summary>What happened when the export ran.</summary>
public class PowerBiExportOutcome
{
    public PowerBiExportResultKind Kind { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>The CSV written, or the output folder when several were.</summary>
    public string? Path { get; set; }

    /// <summary>Rows written, as the tool reported it.</summary>
    public string? RowCount { get; set; }
}
