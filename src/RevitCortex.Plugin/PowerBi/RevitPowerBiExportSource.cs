using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// The export window's link to a live Revit document: discovery, previews and
/// the export itself. All the Revit API the window used to call directly lives
/// here, so the window stays a view that can be built without Revit.
///
/// The window is opened from an IExternalCommand and is modeless but owned by
/// Revit's main thread, so every member is called on that thread.
/// </summary>
public sealed class RevitPowerBiExportSource : IPowerBiExportSource
{
    private readonly Document _doc;
    private readonly ParameterDiscoveryService _discovery = new ParameterDiscoveryService();

    public RevitPowerBiExportSource(Document doc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
    }

    public string DocumentTitle => _doc.Title ?? "";

    public List<CategoryInfo> DiscoverCategories()
        => _discovery.DiscoverCategories(_doc, CancellationToken.None);

    public List<ScheduleInfo> DiscoverSchedules()
        => _discovery.DiscoverSchedules(_doc, CancellationToken.None);

    public List<ParameterInfo> DiscoverParameters(IEnumerable<string> categoryOstCodes, bool includeTypeParameters)
        => _discovery.DiscoverParameters(_doc, categoryOstCodes, includeTypeParameters, sampleSize: 200);

    public PowerBiScopeFilter ReadScope(PowerBiScope scope, IEnumerable<string> categoryOstCodes)
    {
        var filter = new PowerBiScopeFilter();
        var codes = categoryOstCodes.ToList();

        if (scope == PowerBiScope.WholeModel)
        {
            foreach (var code in codes) filter.Categories.Add(code);
            return filter;
        }

        // One pass over the scope: which category ids are present in it.
        // BuildSelectionCollector returns null when nothing is selected; then
        // no category is in scope (never fall back to the whole model).
        FilteredElementCollector? collector = scope == PowerBiScope.ActiveView
            ? new FilteredElementCollector(_doc, _doc.ActiveView.Id)
            : BuildSelectionCollector();

        if (collector == null)
        {
            filter.SelectionEmpty = scope == PowerBiScope.Selection;
            return filter;
        }

        var presentIds = new HashSet<int>();
        foreach (var elem in collector.WhereElementIsNotElementType())
        {
            if (elem.Category?.Id is { } categoryId)
                presentIds.Add((int)GetIdValue(categoryId));
        }

        foreach (var code in codes)
        {
            if (Enum.TryParse<BuiltInCategory>(code, out var bic) && presentIds.Contains((int)bic))
                filter.Categories.Add(code);
        }
        return filter;
    }

    public List<ScheduleFieldInfo>? GetScheduleFields(long scheduleId)
    {
        if (_doc.GetElement(ToElementId(scheduleId)) is not ViewSchedule view) return null;

        var fields = new List<ScheduleFieldInfo>();
        var definition = view.Definition;
        int count = definition.GetFieldCount();
        for (int i = 0; i < count; i++)
        {
            var field = definition.GetField(i);
            if (field.IsHidden) continue;

            var header = string.IsNullOrWhiteSpace(field.ColumnHeading)
                ? field.GetName()
                : field.ColumnHeading;
            fields.Add(new ScheduleFieldInfo
            {
                Header = header ?? "",
                Scope = field.FieldType == ScheduleFieldType.ElementType ? "Type" : "Instance",
                IsReadOnly = field.FieldType != ScheduleFieldType.Instance
                          && field.FieldType != ScheduleFieldType.ElementType,
            });
        }
        return fields;
    }

    public PowerBiPreview PreviewElements(PowerBiScope scope, IList<string> categoryOstCodes,
        IList<string> instanceParameters, IList<string> typeParameters, int take)
    {
        var preview = new PowerBiPreview();

        foreach (var elem in CollectElements(scope, categoryOstCodes, take))
        {
            var row = new string[4 + instanceParameters.Count + typeParameters.Count];
            var typeId = elem.GetTypeId();
            var typeElem = typeId != ElementId.InvalidElementId ? _doc.GetElement(typeId) : null;

            row[0] = GetIdValue(elem.Id).ToString();
            row[1] = elem.Category?.Name ?? "";
            row[2] = (typeElem as ElementType)?.FamilyName ?? "";
            row[3] = (typeElem as ElementType)?.Name ?? "";

            int col = 4;
            foreach (var name in instanceParameters)
            {
                var p = elem.LookupParameter(name);
                row[col++] = p != null ? GetParamDisplay(p) : "";
            }
            foreach (var name in typeParameters)
            {
                var p = typeElem?.LookupParameter(name);
                row[col++] = p != null ? GetParamDisplay(p) : "";
            }
            preview.Rows.Add(row);
        }

        preview.TotalRows = CountElements(scope, categoryOstCodes);
        return preview;
    }

    public PowerBiPreview PreviewSchedule(long scheduleId, int take)
    {
        var preview = new PowerBiPreview();
        if (_doc.GetElement(ToElementId(scheduleId)) is not ViewSchedule view) return preview;

        // Columns come from the schedule's field definition (authoritative, and
        // the same the CSV export uses). Body cells are then read defensively.
        var definition = view.Definition;
        int fieldCount = definition?.GetFieldCount() ?? 0;
        for (int i = 0; i < fieldCount; i++)
        {
            try
            {
                var field = definition!.GetField(i);
                if (field == null || field.IsHidden) continue;
                var heading = string.IsNullOrWhiteSpace(field.ColumnHeading)
                    ? (field.GetName() ?? $"Col{i + 1}")
                    : field.ColumnHeading;
                preview.Headers.Add(MakeUnique(preview.Headers, heading));
            }
            catch { /* skip a malformed field */ }
        }
        if (preview.Headers.Count == 0) return preview;

        var body = view.GetTableData().GetSectionData(SectionType.Body);
        int bodyColumns = body?.NumberOfColumns ?? 0;
        int bodyRows = body?.NumberOfRows ?? 0;
        preview.TotalRows = Math.Max(0, bodyRows - 1);

        // Row 0 of the body is the header row. Body cell indices follow the
        // visible fields (Revit collapses the hidden ones).
        int lastRow = Math.Min(bodyRows, take + 1);
        int columns = Math.Min(preview.Headers.Count, bodyColumns);
        for (int r = 1; r < lastRow; r++)
        {
            var row = new string[preview.Headers.Count];
            for (int c = 0; c < row.Length; c++)
            {
                if (c >= columns) { row[c] = ""; continue; }
                try { row[c] = view.GetCellText(SectionType.Body, r, c) ?? ""; }
                catch { row[c] = ""; }
            }
            preview.Rows.Add(row);
        }
        return preview;
    }

    public PowerBiExportOutcome Export(PowerBiExportProfile profile, PowerBiScope scope)
    {
        // Direct router call: we are on the Revit main thread and the router
        // runs the tool synchronously. No TCP round trip, and no risk of
        // waiting on a socket while holding the UI thread.
        var router = RevitCortexApp.Instance?.Router;
        if (router == null)
            return new PowerBiExportOutcome { Kind = PowerBiExportResultKind.RouterUnavailable };

        var input = new JObject
        {
            ["maxElements"] = profile.MaxElements,
            ["scopeMode"] = profile.ScopeMode
        };

        if (scope == PowerBiScope.Selection)
        {
            var ids = RevitCortexApp.Instance?.UiApplication?.ActiveUIDocument?.Selection.GetElementIds();
            if (ids != null && ids.Count > 0)
                input["selectionIds"] = new JArray(ids.Select(GetIdValue).Cast<object>().ToArray());
        }
        else if (scope == PowerBiScope.ActiveView)
        {
            input["activeViewId"] = GetIdValue(_doc.ActiveView.Id);
        }

        if (profile.UseSchedules)
        {
            input["scheduleIds"] = new JArray(profile.ScheduleIds);
        }
        else
        {
            input["categories"] = new JArray(profile.Categories);
            input["includeTypeParameters"] = profile.IncludeTypeParameters;
            if (profile.InstanceParameters.Count > 0 || profile.TypeParameters.Count > 0)
                input["parameterNames"] = new JArray(profile.InstanceParameters.Concat(profile.TypeParameters));
        }

        if (!string.IsNullOrEmpty(profile.OutputFolder)) input["outputFolder"] = profile.OutputFolder;
        if (!string.IsNullOrEmpty(profile.FileName))
        {
            input["fileName"] = profile.OverwriteFile
                ? profile.FileName
                : InsertTimestamp(profile.FileName!);
        }

        // Column types are opt-in: only sent when the user picked something
        // other than Auto. The tool then writes "_Raw" companion columns and a
        // .pq file with explicit Table.TransformColumnTypes.
        if (!string.Equals(profile.SchemaMappingMode, "Auto", StringComparison.OrdinalIgnoreCase)
            && profile.ColumnTypes != null && profile.ColumnTypes.Count > 0)
        {
            input["schemaMappingMode"] = profile.SchemaMappingMode;
            input["columnTypes"] = JArray.FromObject(profile.ColumnTypes);
        }

        var result = router.Route("push_to_powerbi", input);
        if (result == null)
            return new PowerBiExportOutcome { Kind = PowerBiExportResultKind.NoResponse };

        if (!result.Success)
        {
            return new PowerBiExportOutcome
            {
                Kind = PowerBiExportResultKind.Failed,
                ErrorCode = result.Error?.Code.ToString(),
                ErrorMessage = result.Error?.Message,
            };
        }

        var outcome = new PowerBiExportOutcome { Kind = PowerBiExportResultKind.Succeeded };
        try
        {
            var json = JObject.FromObject(result.Data!);
            outcome.Path = json["filePath"]?.ToString() ?? json["outputFolder"]?.ToString();
            outcome.RowCount = json["elementCount"]?.ToString() ?? json["rowCount"]?.ToString();
        }
        catch { /* the export itself succeeded; the details are optional */ }
        return outcome;
    }

    public void SetAutoExport(PowerBiExportProfile? profile)
    {
        if (profile != null) AutoExportHook.Enable(profile, _doc);
        else AutoExportHook.Disable();
    }

    public void ShowError(string title, string message, string? details)
    {
        try
        {
            var dialog = new Autodesk.Revit.UI.TaskDialog(title)
            {
                MainInstruction = title,
                MainContent = message,
                ExpandedContent = details ?? "",
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Close
            };
            dialog.Show();
        }
        catch { /* never let the error dialog hide the original error path */ }
    }

    public bool ShowExportDone(string title, string instruction, string detail,
        string openFolderLabel, string openFolderHint)
    {
        try
        {
            var dialog = new Autodesk.Revit.UI.TaskDialog(title)
            {
                MainInstruction = instruction,
                MainContent = detail,
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Close,
                DefaultButton = Autodesk.Revit.UI.TaskDialogResult.Close
            };
            dialog.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink1,
                openFolderLabel, openFolderHint);
            return dialog.Show() == Autodesk.Revit.UI.TaskDialogResult.CommandLink1;
        }
        catch
        {
            return false;
        }
    }

    // ── Collectors ───────────────────────────────────────────────────────

    private List<Element> CollectElements(PowerBiScope scope, IList<string> categoryOstCodes, int take)
    {
        var elements = new List<Element>();
        foreach (var code in categoryOstCodes)
        {
            if (elements.Count >= take) break;
            if (!Enum.TryParse<BuiltInCategory>(code, out var bic)) continue;

            var collector = CollectorFor(scope);
            if (collector == null) continue; // "selection" with nothing selected
            elements.AddRange(collector.OfCategory(bic).WhereElementIsNotElementType()
                .Take(take - elements.Count));
        }
        return elements;
    }

    private int CountElements(PowerBiScope scope, IList<string> categoryOstCodes)
    {
        int total = 0;
        foreach (var code in categoryOstCodes)
        {
            if (!Enum.TryParse<BuiltInCategory>(code, out var bic)) continue;

            var collector = CollectorFor(scope);
            if (collector == null) continue;
            total += collector.OfCategory(bic).WhereElementIsNotElementType().GetElementCount();
        }
        return total;
    }

    private FilteredElementCollector? CollectorFor(PowerBiScope scope)
    {
        try
        {
            switch (scope)
            {
                case PowerBiScope.ActiveView: return new FilteredElementCollector(_doc, _doc.ActiveView.Id);
                case PowerBiScope.Selection: return BuildSelectionCollector();
                default: return new FilteredElementCollector(_doc);
            }
        }
        catch
        {
            return new FilteredElementCollector(_doc);
        }
    }

    /// <summary>
    /// A collector over the current Revit selection, or null when nothing is
    /// selected. Callers must read null as "no elements", never as "the whole
    /// model".
    /// </summary>
    private FilteredElementCollector? BuildSelectionCollector()
    {
        var selection = RevitCortexApp.Instance?.UiApplication?.ActiveUIDocument?.Selection.GetElementIds();
        if (selection != null && selection.Count > 0)
            return new FilteredElementCollector(_doc, selection);
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string MakeUnique(List<string> existing, string baseName)
    {
        if (!existing.Contains(baseName)) return baseName;
        int i = 2;
        while (existing.Contains($"{baseName} ({i})")) i++;
        return $"{baseName} ({i})";
    }

    private static string InsertTimestamp(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName);
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        return $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
    }

    private static string GetParamDisplay(Parameter p)
    {
        if (!p.HasValue) return "";
        switch (p.StorageType)
        {
            case StorageType.String: return p.AsString() ?? "";
            case StorageType.Integer: return p.AsInteger().ToString();
            case StorageType.Double: return p.AsValueString() ?? p.AsDouble().ToString("F4");
            case StorageType.ElementId: return p.AsValueString() ?? p.AsElementId().ToString();
            default: return "";
        }
    }

    private static long GetIdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    private static ElementId ToElementId(long value)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(value);
#else
        return new ElementId((int)value);
#endif
    }
}
