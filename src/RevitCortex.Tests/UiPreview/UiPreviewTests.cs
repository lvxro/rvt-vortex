using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RevitCortex.Core.Session;
using RevitCortex.Plugin.PowerBi;
using RevitCortex.Plugin.UI;
using RevitCortex.Plugin.Updates;
using Xunit;
// System.Reflection has a ParameterInfo too.
using PbiParameterInfo = RevitCortex.Plugin.PowerBi.ParameterInfo;

namespace RevitCortex.Tests.UiPreview;

/// <summary>
/// Builds every plugin window without Revit, in the states a user can meet.
///
/// Always: constructs each window and lays it out, so a XAML mistake (a missing
/// resource key, a broken template, a renamed element) fails here instead of
/// when someone opens the window inside Revit.
///
/// With UI_PREVIEW_DIR set (the "UI preview" workflow): also shows each window
/// and saves it as a PNG in that folder, so a change to the interface can be
/// reviewed as pictures. UI_PREVIEW_LANG picks the language (en / es / it).
/// </summary>
public class UiPreviewTests
{
    private static readonly string? OutputDir = Environment.GetEnvironmentVariable("UI_PREVIEW_DIR");
    private static readonly string? Language = Environment.GetEnvironmentVariable("UI_PREVIEW_LANG");
    private static bool Capturing => !string.IsNullOrWhiteSpace(OutputDir);

    // A neutral mid-grey behind the floating (transparent) windows, about what
    // a Revit view looks like behind them, so their shadows are visible.
    private static readonly Color Backdrop = Color.FromRgb(0x6B, 0x72, 0x7C);

    private static readonly DateTime Now = new DateTime(2026, 3, 9, 14, 37, 20);

    // What a resizable window's border and title bar take from its size on
    // Windows 10 and 11 at 100 %: 1200 x 720 leaves a 1184 x 681 client area.
    private const double WindowFrameWidth = 16;
    private const double WindowFrameHeight = 39;

    [Fact]
    public void Every_window_builds_in_every_state()
    {
        var failures = new List<string>();

        RunOnStaThread(() =>
        {
            if (Capturing)
            {
                Directory.CreateDirectory(OutputDir!);
                if (!string.IsNullOrWhiteSpace(Language)) ForceLanguage(Language!);
            }

            var scenarios = new (string Name, Action Run)[]
            {
                ("ribbon-icons", RibbonIcons),
                ("settings-general", SettingsGeneral),
                ("settings-general-on-update", SettingsGeneralRunningWithUpdate),
                ("settings-tools", SettingsTools),
                ("settings-tools-search", SettingsToolsSearch),
                ("update-states", UpdateStates),
                ("pill-states", PillStates),
                ("pill-panel", PillPanel),
                ("autopilot-summary", AutopilotSummary),
                ("powerbi-data", PowerBiData),
                ("powerbi-data-narrow", PowerBiDataNarrow),
                ("powerbi-data-schedules", PowerBiDataSchedules),
                ("powerbi-output", PowerBiOutput),
                ("powerbi-output-advanced", PowerBiOutputAdvanced),
                ("powerbi-output-schedules", PowerBiOutputSchedules),
                ("powerbi-profile-dialogs", PowerBiProfileDialogs),
            };

            foreach (var (name, run) in scenarios)
            {
                // One broken screen must not hide the others.
                try { run(); }
                catch (Exception ex) { failures.Add($"{name}: {ex}"); }
            }

            SetLatestUpdate(null);
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });

        // Next to the pictures, so a failed render explains itself there too.
        if (Capturing && failures.Count > 0)
            File.WriteAllText(Path.Combine(OutputDir!, "failures.txt"),
                string.Join(Environment.NewLine + Environment.NewLine, failures));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    // ── Scenarios ────────────────────────────────────────────────────────

    private static void RibbonIcons()
    {
        // The ribbon itself belongs to Revit; the plugin only supplies these
        // bitmaps. Shown on Revit's light and dark ribbon colors, 32 px (large
        // buttons) and 16 px (stacked buttons), enlarged 3x.
        var large = new[]
        {
            IconFactory.CreateConnectionIcon(32, false),
            IconFactory.CreateConnectionIcon(32, true),
            IconFactory.CreateAutopilotIcon(32, false),
            IconFactory.CreateAutopilotIcon(32, true),
        };
        var small = new[]
        {
            IconFactory.CreateSettingsIcon(16),
            IconFactory.CreatePowerBiIcon(16),
            IconFactory.CreateSupportIcon(16),
            IconFactory.CreateConnectionIcon(16, false),
            IconFactory.CreateConnectionIcon(16, true),
            IconFactory.CreateAutopilotIcon(16, false),
            IconFactory.CreateAutopilotIcon(16, true),
        };
        if (!Capturing) return;

        const double zoom = 3, gap = 14, pad = 18;
        double rowHeight = pad + 32 * zoom + gap + 16 * zoom + pad;
        double width = pad * 2 + small.Length * (32 * zoom + gap);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            var ribbons = new[] { Color.FromRgb(0xF5, 0xF5, 0xF5), Color.FromRgb(0x3B, 0x44, 0x53) };
            for (int r = 0; r < ribbons.Length; r++)
            {
                double top = r * rowHeight;
                dc.DrawRectangle(new SolidColorBrush(ribbons[r]), null, new Rect(0, top, width, rowHeight));
                double x = pad;
                foreach (var icon in large)
                {
                    dc.DrawImage(icon, new Rect(x, top + pad, 32 * zoom, 32 * zoom));
                    x += 32 * zoom + gap;
                }
                x = pad;
                foreach (var icon in small)
                {
                    dc.DrawImage(icon, new Rect(x, top + pad + 32 * zoom + gap, 16 * zoom, 16 * zoom));
                    x += 16 * zoom + gap;
                }
            }
        }
        SavePng(RenderVisual(visual, width, rowHeight * 2, 1), "ribbon-icons");
    }

    private static void SettingsGeneral()
    {
        SetLatestUpdate(null);
        var window = new SettingsWindow();
        Build(window, "settings-general");
    }

    private static void SettingsGeneralRunningWithUpdate()
    {
        SetLatestUpdate(SampleUpdate());
        var window = new SettingsWindow();
        window.GeneralPage.ApplyServerState(running: true, port: 8080);
        Build(window, "settings-general-on-update");
        SetLatestUpdate(null);
    }

    private static void SettingsTools()
    {
        var window = new SettingsWindow();
        window.ToolsPage.SetTools(SampleTools());
        window.ShowToolsTab();
        Build(window, "settings-tools", beforeCapture: () =>
        {
            // Open the first category, as a user would.
            var list = (ItemsControl)window.ToolsPage.FindName("CategoryList");
            list.ItemsSource.Cast<CategoryGroup>().First().ToggleExpanded();
        });
    }

    private static void SettingsToolsSearch()
    {
        var window = new SettingsWindow();
        window.ToolsPage.SetTools(SampleTools());
        window.ShowToolsTab();
        Build(window, "settings-tools-search", beforeCapture: () =>
        {
            ((TextBox)window.ToolsPage.FindName("SearchBox")).Text = "param";
        });
    }

    private static void UpdateStates()
    {
        SetLatestUpdate(SampleUpdate());
        var shots = new List<BitmapSource?>();

        // Idle (an update is available), then the three later states, set the
        // way the window sets them itself.
        foreach (var state in new[] { "Idle", "Downloading", "ConfirmInstall", "Error" })
        {
            var window = new UpdateNotificationWindow();
            if (state == "Downloading") SetDownloadProgress(59_600_000, 96_100_000);
            if (state != "Idle") SetUpdateWindowState(window, state);
            shots.Add(Build(window, null));
        }

        SetDownloadProgress(0, 0);
        SetLatestUpdate(null);
        SaveSheet(shots, "update-states");
    }

    private static void PillStates()
    {
        var saved = Now.AddMinutes(-5);
        var approved = new AutopilotEvent(Now.AddMinutes(-2), AutopilotEventKind.Approved,
            "Aprobado: modificar parámetros (48)");
        var declined = new AutopilotEvent(Now.AddMinutes(-1), AutopilotEventKind.Declined,
            "Rechazado: ejecutar script C# (1)");

        var states = new (bool Autopilot, AutopilotPillState State)[]
        {
            // Plain Auto mode (the user is present and clicked "Auto").
            (false, new AutopilotPillState { Now = Now }),
            // Autopilot, nothing has happened yet.
            (true, new AutopilotPillState
            {
                Autopilot = true, Now = Now,
                Activity = Snapshot(0, 0, null, null),
            }),
            // Working: scripts allowed, model saved.
            (true, new AutopilotPillState
            {
                Autopilot = true, ScriptsAllowed = true, Now = Now,
                Activity = Snapshot(12, 0, saved, Now.AddSeconds(-3), approved),
            }),
            // Changes not saved yet.
            (true, new AutopilotPillState
            {
                Autopilot = true, SavePending = true, Now = Now,
                Activity = Snapshot(13, 0, saved, Now.AddSeconds(-40), approved),
            }),
            // One step was declined.
            (true, new AutopilotPillState
            {
                Autopilot = true, Now = Now,
                Activity = Snapshot(12, 1, saved, Now.AddSeconds(-40), approved, declined),
            }),
            // Warning: another document is active.
            (true, new AutopilotPillState
            {
                Autopilot = true, ApprovalsPaused = true, Now = Now,
                Activity = Snapshot(12, 0, saved, null, approved),
            }),
            // Warning: the server is off.
            (true, new AutopilotPillState
            {
                Autopilot = true, ServerOff = true, Now = Now,
                Activity = Snapshot(12, 0, saved, null, approved),
            }),
        };

        var shots = new List<BitmapSource?>();
        foreach (var (autopilot, state) in states)
        {
            var window = new AutoModeWindow(IntPtr.Zero);
            window.SetMode(autopilot);
            window.Render(state);
            shots.Add(Build(window, null, beforeCapture: () => window.Render(state),
                close: window.CloseFromHost));
        }
        SaveSheet(shots, "pill-states");
    }

    private static void PillPanel()
    {
        var events = new[]
        {
            new AutopilotEvent(Now.AddMinutes(-6), AutopilotEventKind.Approved, "Aprobado: renombrar (12)"),
            new AutopilotEvent(Now.AddMinutes(-5), AutopilotEventKind.Saved, "Modelo guardado"),
            new AutopilotEvent(Now.AddMinutes(-4), AutopilotEventKind.DialogClosed, "Aviso de Revit cerrado con Cancelar"),
            new AutopilotEvent(Now.AddMinutes(-2), AutopilotEventKind.Approved, "Aprobado: modificar parámetros (48)"),
            new AutopilotEvent(Now.AddMinutes(-1), AutopilotEventKind.Declined, "Rechazado: ejecutar script C# (1)"),
        };
        var state = new AutopilotPillState
        {
            Autopilot = true, ScriptsAllowed = true, Now = Now,
            // RecentEvents is newest first.
            Activity = new AutopilotSnapshot(Now.AddMinutes(-47), 12, 1, 1, Now.AddMinutes(-5),
                Now.AddMinutes(-1), Now.AddSeconds(-4), Enumerable.Reverse(events).ToArray(), new[] { events[4] }),
        };

        var window = new AutoModeWindow(IntPtr.Zero);
        window.SetMode(true);
        window.SetExpanded(true);
        window.Render(state);
        Build(window, "pill-panel", beforeCapture: () => window.Render(state), close: window.CloseFromHost);
    }

    private static void AutopilotSummary()
    {
        var declined = new[]
        {
            new AutopilotEvent(Now.AddMinutes(-38), AutopilotEventKind.Declined, "Rechazado: ejecutar script C# (1)"),
            new AutopilotEvent(Now.AddMinutes(-16), AutopilotEventKind.Declined, "Rechazado: eliminar elementos (6)"),
        };
        var withPending = new AutopilotSnapshot(Now.AddMinutes(-72), 31, 2, 3, Now.AddMinutes(-2),
            Now, Now, Array.Empty<AutopilotEvent>(), declined);
        var clean = new AutopilotSnapshot(Now.AddMinutes(-25), 9, 0, 0, Now.AddMinutes(-1),
            Now, Now, Array.Empty<AutopilotEvent>(), Array.Empty<AutopilotEvent>());

        var shots = new List<BitmapSource?>
        {
            Build(new AutopilotSummaryWindow(withPending, Now, IntPtr.Zero), null),
            Build(new AutopilotSummaryWindow(clean, Now, IntPtr.Zero), null),
        };
        SaveSheet(shots, "autopilot-summary");
    }

    // Power BI export. The window reads the model through IPowerBiExportSource,
    // so here it gets a sample model instead of Revit; a profile puts it in the
    // state a user would have reached by hand.

    private static void PowerBiData()
    {
        var window = PowerBiWindow(SampleExport());
        Build(window, "powerbi-data", atDesignSize: true, beforeCapture: () =>
        {
            // One row picked in the available list, about to be added.
            ((ListBox)window.FindName("AvailableList")).SelectedIndex = 3;
        });
    }

    private static void PowerBiDataNarrow()
    {
        // Shown for real: on the CI screen (1024 px) Windows shrinks the
        // window to its minimum width, the tightest layout a user can get.
        var window = PowerBiWindow(SampleExport());
        Build(window, "powerbi-data-narrow");
    }

    private static void PowerBiDataSchedules()
    {
        var window = PowerBiWindow(SampleScheduleExport());
        Build(window, "powerbi-data-schedules", atDesignSize: true);
    }

    private static void PowerBiOutput()
    {
        var window = PowerBiWindow(SampleExport());
        Assert.True(window.GoToOutput(), "Step 2 did not open for a complete export.");
        Build(window, "powerbi-output", atDesignSize: true);
    }

    private static void PowerBiOutputAdvanced()
    {
        // Refresh on (shows the workspace and dataset fields) and the column
        // types open in "Suggested".
        var export = SampleExport();
        export.SchemaMappingMode = "Suggested";
        export.TriggerPbiRefresh = true;
        var window = PowerBiWindow(export);
        Assert.True(window.GoToOutput(), "Step 2 did not open for a complete export.");
        window.ExpandAdvanced();
        Build(window, "powerbi-output-advanced", atDesignSize: true);
    }

    private static void PowerBiOutputSchedules()
    {
        var window = PowerBiWindow(SampleScheduleExport());
        Assert.True(window.GoToOutput(), "Step 2 did not open for a schedule export.");
        Build(window, "powerbi-output-schedules", atDesignSize: true);
    }

    private static void PowerBiProfileDialogs()
    {
        var older = SampleScheduleExport();
        older.LastUsed = Now.AddDays(-12).ToUniversalTime();
        var shots = new List<BitmapSource?>
        {
            Build(new ProfileNameDialog(), null),
            Build(new ProfilePickerDialog(new List<PowerBiExportProfile> { SampleExport(), older }), null),
        };
        SaveSheet(shots, "powerbi-profile-dialogs");
    }

    /// <summary>The export window on the sample model, with a profile applied.</summary>
    private static PowerBiExportWindow PowerBiWindow(PowerBiExportProfile profile)
    {
        var window = new PowerBiExportWindow(new SamplePowerBiSource());
        window.ApplyProfile(profile);
        // "Profile loaded" is a passing message; show the footer at rest.
        typeof(PowerBiExportWindow).GetMethod("ClearStatus", Hidden)!.Invoke(window, null);
        return window;
    }

    private static PowerBiExportProfile SampleExport() => new PowerBiExportProfile
    {
        Name = "Mediciones",
        Categories = { "OST_Walls", "OST_Floors", "OST_Doors", "OST_Rooms" },
        InstanceParameters = { "Nivel", "Marca", "Área", "Volumen" },
        TypeParameters = { "Nombre de tipo" },
        IncludeTypeParameters = true,
        OutputFolder = @"C:\Proyectos\Torre Norte\Power BI",
        FileName = "Torre Norte.csv",
        LastUsed = Now.AddHours(-3).ToUniversalTime(),
    };

    private static PowerBiExportProfile SampleScheduleExport() => new PowerBiExportProfile
    {
        Name = "Tablas para el panel",
        UseSchedules = true,
        ScheduleIds = { 502, 505 },
        OutputFolder = @"C:\Proyectos\Torre Norte\Power BI",
    };

    /// <summary>A small model in Spanish, in place of Revit.</summary>
    private sealed class SamplePowerBiSource : IPowerBiExportSource
    {
        private static readonly string[] Levels = { "Nivel 1", "Nivel 1", "Nivel 2", "Nivel 2", "Nivel 3" };

        private static readonly Dictionary<string, string[]> Values = new()
        {
            ["Nivel"] = Levels,
            ["Marca"] = new[] { "M-101", "M-102", "M-201", "M-202", "M-301" },
            ["Área"] = new[] { "18,40 m²", "22,75 m²", "18,40 m²", "31,20 m²", "12,05 m²" },
            ["Volumen"] = new[] { "3,68 m³", "4,55 m³", "2,76 m³", "6,24 m³", "1,81 m³" },
            ["Nombre de tipo"] = new[]
            {
                "Genérico - 200 mm", "Genérico - 200 mm", "Ladrillo - 150 mm", "Genérico - 200 mm", "Ladrillo - 150 mm",
            },
        };

        public string DocumentTitle => "Torre Norte.rvt";

        public List<CategoryInfo> DiscoverCategories() => new List<CategoryInfo>
        {
            Category("OST_StructuralFraming", "Armazón estructural", 210),
            Category("OST_Roofs", "Cubiertas", 6),
            Category("OST_Stairs", "Escaleras", 8),
            Category("OST_Rooms", "Habitaciones", 57),
            Category("OST_Furniture", "Mobiliario", 233),
            Category("OST_Walls", "Muros", 412),
            Category("OST_StructuralColumns", "Pilares estructurales", 64),
            Category("OST_Doors", "Puertas", 126),
            Category("OST_Floors", "Suelos", 38),
            Category("OST_Ceilings", "Techos", 41),
            Category("OST_Windows", "Ventanas", 94),
            Category("OST_Dimensions", "Cotas", 1830, "Annotation"),
            Category("OST_RoomTags", "Etiquetas de habitación", 57, "Annotation"),
            Category("OST_TextNotes", "Notas de texto", 112, "Annotation"),
        };

        public List<ScheduleInfo> DiscoverSchedules() => new List<ScheduleInfo>
        {
            Schedule(501, "Lista de planos", "", 24),
            Schedule(502, "Tabla de planificación de habitaciones", "Habitaciones", 57),
            Schedule(503, "Tabla de planificación de mobiliario", "Mobiliario", 233),
            Schedule(504, "Tabla de planificación de muros", "Muros", 38),
            Schedule(505, "Tabla de planificación de puertas", "Puertas", 126),
            Schedule(506, "Tabla de planificación de ventanas", "Ventanas", 94),
        };

        public PowerBiScopeFilter ReadScope(PowerBiScope scope, IEnumerable<string> categoryOstCodes)
        {
            var filter = new PowerBiScopeFilter();
            foreach (var code in categoryOstCodes) filter.Categories.Add(code);
            return filter;
        }

        public List<PbiParameterInfo> DiscoverParameters(IEnumerable<string> categoryOstCodes, bool includeTypeParameters)
        {
            var parameters = new List<PbiParameterInfo>
            {
                Parameter("Área", "Cotas", 96, readOnly: true),
                Parameter("Comentarios", "Datos de identidad", 12),
                Parameter("Desfase de base", "Restricciones", 65),
                Parameter("Fase de creación", "Proceso por fases", 100),
                Parameter("Fase de derribo", "Proceso por fases", 0),
                Parameter("Longitud", "Cotas", 65, readOnly: true),
                Parameter("Marca", "Datos de identidad", 71),
                Parameter("Nivel", "Restricciones", 100),
                Parameter("Restricción de base", "Restricciones", 65),
                Parameter("Volumen", "Cotas", 71, readOnly: true),
            };
            if (includeTypeParameters)
            {
                parameters.Add(Parameter("Descripción", "Datos de identidad", 34, type: true));
                parameters.Add(Parameter("Función", "Construcción", 65, type: true));
                parameters.Add(Parameter("Marca de tipo", "Datos de identidad", 48, type: true));
                parameters.Add(Parameter("Nombre de tipo", "Datos de identidad", 100, type: true));
            }
            return parameters;
        }

        public List<ScheduleFieldInfo>? GetScheduleFields(long scheduleId) => new List<ScheduleFieldInfo>
        {
            new ScheduleFieldInfo { Header = "Marca" },
            new ScheduleFieldInfo { Header = "Nivel" },
            new ScheduleFieldInfo { Header = "Anchura", Scope = "Type" },
            new ScheduleFieldInfo { Header = "Altura", Scope = "Type" },
            new ScheduleFieldInfo { Header = "Nombre de tipo", Scope = "Type" },
            new ScheduleFieldInfo { Header = "Recuento", IsReadOnly = true },
        };

        public PowerBiPreview PreviewElements(PowerBiScope scope, IList<string> categoryOstCodes,
            IList<string> instanceParameters, IList<string> typeParameters, int take)
        {
            var preview = new PowerBiPreview { TotalRows = 633 };
            var names = instanceParameters.Concat(typeParameters).ToList();
            for (int r = 0; r < Math.Min(take, 5); r++)
            {
                var row = new List<string>
                {
                    (348112 + r * 37).ToString(), "Muros", "Muro básico", Values["Nombre de tipo"][r],
                };
                row.AddRange(names.Select(n => Values.TryGetValue(n, out var cells) ? cells[r] : ""));
                preview.Rows.Add(row.ToArray());
            }
            return preview;
        }

        public PowerBiPreview PreviewSchedule(long scheduleId, int take) => new PowerBiPreview
        {
            Headers = { "Número", "Nombre", "Nivel", "Área" },
            Rows =
            {
                new[] { "101", "Vestíbulo", "Nivel 1", "42,10 m²" },
                new[] { "102", "Recepción", "Nivel 1", "18,65 m²" },
                new[] { "103", "Sala de reuniones", "Nivel 1", "24,30 m²" },
                new[] { "201", "Oficina abierta", "Nivel 2", "96,80 m²" },
                new[] { "202", "Archivo", "Nivel 2", "11,45 m²" },
            },
            TotalRows = 57,
        };

        // The previews never export or open a Revit dialog.
        public PowerBiExportOutcome Export(PowerBiExportProfile profile, PowerBiScope scope)
            => new PowerBiExportOutcome { Kind = PowerBiExportResultKind.RouterUnavailable };

        public void SetAutoExport(PowerBiExportProfile? profile) { }

        public void ShowError(string title, string message, string? details) { }

        public bool ShowExportDone(string title, string instruction, string detail,
            string openFolderLabel, string openFolderHint) => false;

        private static CategoryInfo Category(string code, string name, int count, string type = "Model")
            => new CategoryInfo { OstCode = code, DisplayName = name, InstanceCount = count, CategoryType = type };

        private static ScheduleInfo Schedule(long id, string name, string category, int rows)
            => new ScheduleInfo { ScheduleId = id, Name = name, CategoryName = category, RowCount = rows };

        private static PbiParameterInfo Parameter(string name, string group, int coverage,
            bool type = false, bool readOnly = false)
            => new PbiParameterInfo
            {
                Name = name,
                GroupName = group,
                CoveragePercent = coverage,
                Scope = type ? "Type" : "Instance",
                IsReadOnly = readOnly,
            };
    }

    // ── Sample data ──────────────────────────────────────────────────────

    private static AutopilotSnapshot Snapshot(int approved, int declined, DateTime? savedAt,
        DateTime? lastToolCall, params AutopilotEvent[] oldestFirst)
    {
        var newestFirst = Enumerable.Reverse(oldestFirst).ToArray();
        var declinedEvents = oldestFirst.Where(e => e.Kind == AutopilotEventKind.Declined).ToArray();
        DateTime? lastActivity = newestFirst.Length > 0 ? newestFirst[0].Time : lastToolCall;
        return new AutopilotSnapshot(Now.AddMinutes(-47), approved, declined, 0, savedAt,
            lastActivity, lastToolCall, newestFirst, declinedEvents);
    }

    private static UpdateInfo SampleUpdate() => new UpdateInfo(
        new Version(1, 2, 0),
        "https://github.com/lvxro/rvt-vortex/releases/download/v1.2.0/RVT-Vortex-v1.2.0.zip",
        "  - New dark interface for Settings, the update notice and the Autopilot pill.\n" +
        "  - The Autopilot pill shows what was approved, declined and saved.\n" +
        "  - A summary appears when Autopilot stops.",
        hasUpdate: true);

    private static List<(string Name, string Category, string Description, bool IsEnabled)> SampleTools() => new()
    {
        ("create_dimensions", "Annotations", "Creates linear dimension annotations between elementIds (2+) or startPoint/endPoint.", true),
        ("create_text_note", "Annotations", "Creates one or more text notes in the active or specified view.", true),
        ("tag_rooms", "Annotations", "Tags all or specified rooms in the current view.", true),
        ("send_code_to_revit", "Code", "Runs a C# script inside Revit. Last resort; off by default.", false),
        ("get_element_parameters", "Elements", "Returns instance and type parameters for the given element ids.", true),
        ("set_element_parameters", "Elements", "Sets one or more parameters on a single element.", true),
        ("bulk_modify_parameter_values", "Elements", "Sets the same parameter value on many elements, with a dry run first.", true),
        ("delete_element", "Elements", "Deletes elements by id after a confirmation.", true),
        ("ifc_list_export_configurations", "IFC", "Lists the IFC export configurations saved in the document.", true),
        ("check_model_health", "Model", "Returns a health score and the main issues of the active model.", true),
        ("get_warnings", "Model", "Lists the warnings of the active model.", true),
        ("get_shared_parameters", "Parameters", "Lists the shared parameters bound in the document.", true),
        ("get_schedule_data", "Schedules", "Returns the rows of a schedule.", true),
        ("create_view", "Views", "Creates a floor plan, section or 3D view.", true),
    };

    // ── Building and capturing ───────────────────────────────────────────

    /// <summary>
    /// Lays the window out and, when capturing, shows it and returns its
    /// picture (saved as <paramref name="saveAs"/>.png when that is given).
    /// </summary>
    private static BitmapSource? Build(Window window, string? saveAs, Action? beforeCapture = null,
        Action? close = null, bool atDesignSize = false)
    {
        if (Capturing && atDesignSize)
        {
            // The window is wider than the CI screen, and Windows would
            // shrink it when shown. Lay its content out off screen at the
            // size its client area has on a normal monitor, and draw that.
            var content = (FrameworkElement)window.Content;
            var size = new Size(window.Width - WindowFrameWidth, window.Height - WindowFrameHeight);
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
            Pump();
            beforeCapture?.Invoke();
            content.UpdateLayout();
            Pump();

            var ground = window.Background is SolidColorBrush brush ? brush.Color : Backdrop;
            var shot = RenderVisual(content, size.Width, size.Height, 2, ground);
            if (saveAs != null) SavePng(shot, saveAs);
            return shot;
        }

        if (!Capturing)
        {
            // No window on screen: lay out the content on its own.
            var content = (FrameworkElement)window.Content;
            double width = double.IsNaN(window.Width) ? 900 : window.Width;
            double height = double.IsNaN(window.Height) ? 700 : window.Height;
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(content.DesiredSize));
            content.UpdateLayout();
            Pump();
            beforeCapture?.Invoke();
            content.UpdateLayout();
            Pump();
            return null;
        }

        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        try
        {
            Pump();
            beforeCapture?.Invoke();
            window.UpdateLayout();
            Pump();

            // The window's first visual child is its client area: no title
            // bar, and for the floating windows it includes the shadow margin.
            var client = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            bool transparent = window.AllowsTransparency;
            var shot = RenderVisual(client, client.ActualWidth, client.ActualHeight, 2,
                transparent ? Backdrop : (Color?)null);
            if (saveAs != null) SavePng(shot, saveAs);
            return shot;
        }
        finally
        {
            if (close != null) close();
            else window.Close();
            Pump();
        }
    }

    private static BitmapSource RenderVisual(Visual visual, double width, double height, double scale,
        Color? backdrop = null)
    {
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));

        var shot = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        shot.Render(visual);
        shot.Freeze();
        if (backdrop == null) return shot;

        var composed = new DrawingVisual();
        using (var dc = composed.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(backdrop.Value), null, new Rect(0, 0, width, height));
            dc.DrawImage(shot, new Rect(0, 0, width, height));
        }
        var result = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        result.Render(composed);
        result.Freeze();
        return result;
    }

    /// <summary>Stacks several shots into one picture, top to bottom.</summary>
    private static void SaveSheet(List<BitmapSource?> shots, string name)
    {
        var images = shots.Where(s => s != null).Cast<BitmapSource>().ToList();
        if (!Capturing || images.Count == 0) return;

        // Shots are rendered at 2x: PixelWidth is twice the layout width.
        double width = images.Max(i => i.Width);
        double height = images.Sum(i => i.Height);

        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Backdrop), null, new Rect(0, 0, width, height));
            double y = 0;
            foreach (var image in images)
            {
                dc.DrawImage(image, new Rect((width - image.Width) / 2, y, image.Width, image.Height));
                y += image.Height;
            }
        }
        SavePng(RenderVisual(sheet, width, height, 2), name);
    }

    private static void SavePng(BitmapSource image, string name)
    {
        if (!Capturing) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(OutputDir!, name + ".png"));
        encoder.Save(stream);
    }

    /// <summary>Lets queued layout, navigation and Loaded handlers run.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    // ── Reaching into the plugin (it has no InternalsVisibleTo) ───────────

    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private static void ForceLanguage(string locale)
    {
        var localization = typeof(SettingsWindow).Assembly.GetType("RevitCortex.Plugin.UI.Localization", true)!;
        localization.GetField("_cachedLocale", Hidden)!.SetValue(null, locale);
    }

    private static void SetLatestUpdate(UpdateInfo? info) =>
        typeof(UpdateChecker).GetProperty(nameof(UpdateChecker.Latest))!.SetValue(null, info);

    private static void SetDownloadProgress(long received, long total) =>
        typeof(UpdateChecker).GetField("_downloadProgress", Hidden)!.SetValue(null, (received, total));

    private static void SetUpdateWindowState(UpdateNotificationWindow window, string stateName)
    {
        var field = typeof(UpdateNotificationWindow).GetField("_state", Hidden)!;
        field.SetValue(window, Enum.Parse(field.FieldType, stateName));
        typeof(UpdateNotificationWindow).GetMethod("Refresh", Hidden)!.Invoke(window, null);
    }
}
