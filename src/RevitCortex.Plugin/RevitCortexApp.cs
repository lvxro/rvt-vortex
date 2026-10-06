using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Plugin.Caching;
using RevitCortex.Plugin.Communication;
using RevitCortex.Plugin.Discovery;
using RevitCortex.Plugin.PowerBiLive;
using RevitCortex.Plugin.Threading;
using RevitCortex.Plugin.UI;
using System;
using System.Reflection;

namespace RevitCortex.Plugin;

public class RevitCortexApp : IExternalApplication
{
    private SocketService? _socketService;
    private CortexRouter? _router;
    private CortexSession? _session;
    private DocumentChangeWatcher? _cacheWatcher;
    private UIApplication? _uiApplication;
    private int _port = CortexEnvironment.Current.DefaultPort;
    private Autodesk.Revit.UI.PushButton? _connectButton;
    private Autodesk.Revit.UI.PushButton? _autopilotButton;
    private UI.AutoModeWindow? _autoModeWindow;
    private UI.UnattendedDialogHandler? _unattendedDialogHandler;
    private bool _updateNotificationShown;
    private PbiSelectHttpListener? _pbiSelectListener;
    private PbiActionEventHandler? _pbiActionHandler;
    private ExternalEvent? _pbiActionEvent;

    public static RevitCortexApp? Instance { get; private set; }

    /// <summary>
    /// Exception text if OnStartup failed after the ribbon was created. The
    /// ribbon buttons stay visible in that case, so commands show this instead
    /// of a bare "not initialized". Also written to startup-error.log.
    /// </summary>
    public static string? StartupError { get; private set; }

    public static string StartupErrorLogPath =>
        System.IO.Path.Combine(CortexEnvironment.Current.RootFolder, "startup-error.log");

    /// <summary>
    /// Fired on the calling thread whenever the server starts, stops, or crashes.
    /// Subscribers must marshal to the UI thread themselves if needed.
    /// </summary>
    public event Action? ServiceStateChanged;

    /// <summary>
    /// Returns true only if the socket service flag is set AND a live TCP
    /// connection to localhost:port succeeds. This catches cases where the
    /// listener thread died unexpectedly while the flag remained true.
    /// </summary>
    public bool IsServiceRunning
    {
        get
        {
            if (_socketService?.IsRunning != true) return false;
            try
            {
                using var probe = new System.Net.Sockets.TcpClient();
                probe.Connect("127.0.0.1", _port);
                return true;
            }
            catch
            {
                // Listener is gone — sync internal flag so next call is fast
                _socketService.Stop();
                return false;
            }
        }
    }

    public int Port => _port;
    public UIApplication? UiApplication => _uiApplication;
    public CortexRouter? Router => _router;
    public CortexSession? Session => _session;

    public Result OnStartup(UIControlledApplication application)
    {
        Instance = this;

        // Roslyn (send_code_to_revit, net8+) needs Microsoft.CodeAnalysis 4.12 plus its
        // System.Collections.Immutable / System.Reflection.Metadata 8.0 dependencies.
        // Revit loads every add-in into ONE shared AssemblyLoadContext and does not probe
        // our plugin folder for these, so a sibling add-in's older copy (or the missing
        // 8.0 deps) breaks the bind with "Could not load Microsoft.CodeAnalysis 4.12.0.0".
        // Serve our own bundled copies from the plugin folder so they always win.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveBundledDependency;

        try
        {
            // Create ribbon panel
            CreateRibbonPanel(application);

            // Initialize session, router, and tools
            var store = new SessionStore();
            _session = new CortexSession(store);
            _session.ConfirmAction = (action, count, desc) =>
                ConfirmationHelper.ConfirmWithSession(action, count, desc, _session);
            _session.CriticalConfirmAction = ConfirmationHelper.ConfirmCritical;
            _session.AutoModeActivity += OnAutoModeActivity;
            _session.AutoDecision += OnAutoDecision;
            ConfirmationHelper.AutoModeChanged += OnAutoModeChanged;
            var analyzer = new DocumentAnalyzer();

            // One audit logger for the whole plugin, bound to the active
            // profile's audit path (separate file per prod/dev). Shared by the
            // router and the execution handler so no code path falls back to
            // AuditLogger()'s hardcoded prod default.
            var auditLogger = new AuditLogger(CortexEnvironment.Current.AuditLogPath);

            Telemetry.TelemetryBootstrap.Init(application);

            // License gate: built before the router so it can be passed in. Best-effort —
            // a null Gate means no gating (see LicenseBootstrap).
            Licensing.LicenseBootstrap.Init(CortexEnvironment.Current);

            _router = new CortexRouter(_session, analyzer, auditLogger: auditLogger,
                errorReporter: Telemetry.TelemetryBootstrap.Reporter,
                licenseGate: Licensing.LicenseBootstrap.Gate);

            var toolsAssembly = LoadToolsAssembly();
            if (toolsAssembly != null)
            {
                _router.RegisterToolsFromAssembly(toolsAssembly);
            }

            // Also scan the Plugin assembly itself: a few tools (Power BI Live
            // auth/REST) live here because they depend on MSAL.NET which is
            // referenced by the Plugin, not the Tools project.
            _router.RegisterToolsFromAssembly(Assembly.GetExecutingAssembly());

            // Create thread dispatcher for Revit main thread execution
            var executionHandler = new ToolExecutionHandler(auditLogger);
            var externalEvent = ExternalEvent.Create(executionHandler);
            var dispatcher = new RevitThreadDispatcher(executionHandler, externalEvent);
            _router.SetDispatcher(dispatcher);

            // Load disabled tools, read-only mode, and port from settings
            LoadDisabledTools();
            LoadReadOnlyMode();
            LoadPort();

            // Fire-and-forget update check against the public metadata repo.
            // When an update is found, the UpdateAvailable event wakes up the
            // notification window (or OnIdling shows it if Revit is already idle).
            RevitCortex.Plugin.Updates.UpdateChecker.UpdateAvailable += OnUpdateAvailable;
            RevitCortex.Plugin.Updates.UpdateChecker.CheckInBackground();

            // Create the PBI action ExternalEvent (registered once at startup,
            // used whenever the Power BI Desktop visual sends a pbi-* request)
            _pbiActionHandler = new PbiActionEventHandler();
            _pbiActionEvent = ExternalEvent.Create(_pbiActionHandler);

            // Create socket service but do NOT start automatically
            _socketService = new SocketService(_router, _port);

            // Listen for document events
            application.ControlledApplication.DocumentOpened += OnDocumentOpened;
            application.ControlledApplication.DocumentClosing += OnDocumentClosing;

            // Subscribe the tool-result cache to model-change events. The watcher
            // bumps DocumentVersion and drops Document/Transaction entries so
            // cached reads can never outlive the model state they describe.
            _cacheWatcher = new DocumentChangeWatcher(_session);
            _cacheWatcher.Attach(application.ControlledApplication);

            // Capture UIApplication when Revit is idle (needed for ViewActivated hook)
            application.Idling += OnIdling;

            // Unattended mode: auto-dismiss Revit's own modal dialogs so they
            // can't freeze the session while the user is away.
            _unattendedDialogHandler = new UI.UnattendedDialogHandler(() => _session);
            _unattendedDialogHandler.Attach(application);

            // Unattended mode: save the model after changes (throttled).
            application.Idling += OnIdlingAutoSave;

            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Started. {_router.TotalToolCount} tools registered.");

            Telemetry.TelemetryBootstrap.PromptConsentIfNeeded();

            try { if (System.IO.File.Exists(StartupErrorLogPath)) System.IO.File.Delete(StartupErrorLogPath); }
            catch { /* stale log is harmless */ }
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Telemetry.TelemetryBootstrap.Reporter?.Record("_startup", false, "Unknown",
                ex.Message, failureStage: "startup", durationMs: 0, responseBytes: 0);
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Startup failed: {ex}");
            StartupError = ex.ToString();
            try
            {
                System.IO.Directory.CreateDirectory(CortexEnvironment.Current.RootFolder);
                System.IO.File.WriteAllText(StartupErrorLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  RVT Vortex {Assembly.GetExecutingAssembly().GetName().Version}\n{ex}\n");
            }
            catch { /* diagnostics must never throw */ }
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            Telemetry.TelemetryBootstrap.Shutdown();

            ConfirmationHelper.AutoModeChanged -= OnAutoModeChanged;
            _pbiSelectListener?.Dispose();
            _pbiSelectListener = null;
            _socketService?.Stop();
            application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
            application.Idling -= OnIdling;
            _unattendedDialogHandler?.Detach(application);
            application.Idling -= OnIdlingAutoSave;
            _unattendedDialogHandler = null;
            if (_uiApplication != null)
                _uiApplication.ViewActivated -= OnViewActivated;

            _cacheWatcher?.Dispose();
            _cacheWatcher = null;

            // Delete all TEMP scripts generated during this session
            CleanupTempScripts();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Shutdown error: {ex.Message}");
        }
        return Result.Succeeded;
    }

    public void StartService(Document? activeDocument = null)
    {
        if (_socketService != null && !_socketService.IsRunning)
        {
            // Initialize session with active document if available
            if (activeDocument != null && _router != null)
            {
                var locale = LocaleDetector.Detect(activeDocument);
                _router.OnDocumentChanged(activeDocument, locale);
                // Re-store UIApplication after session reinitialize (needed by send_code_to_revit)
                if (_uiApplication != null)
                    _session?.Store.Set("uiApplication", _uiApplication);
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Session initialized with document: {activeDocument.Title}, locale: {locale}");
            }

            _socketService.Start();

            // Start PBI Desktop → Revit HTTP listener on port 27016
            if (_pbiSelectListener == null && _pbiActionHandler != null && _pbiActionEvent != null)
            {
                var handler = _pbiActionHandler;
                // Bind the event so the handler can self-raise after every enqueue.
                // This eliminates the previous shared-pending-state race: each
                // listener callback now hits the handler's queue, gets its own
                // completion event, and times out without polluting later requests.
                handler.BindExternalEvent(_pbiActionEvent);

                _pbiSelectListener = new PbiSelectHttpListener(
                    new PbiSelectHttpListener.Callbacks(
                        selection:       (ids, action) => _uiApplication == null ? null : handler.DispatchSelection(ids, action),
                        color:           items         => _uiApplication == null ? null : handler.DispatchColor(items),
                        resetOverrides:  ()            => _uiApplication == null ? null : handler.DispatchReset(),
                        createView:      (ids, name)   => _uiApplication == null ? null : handler.DispatchCreateView(ids, name),
                        // Rich callbacks: take UniqueIds + DocumentTitle into account.
                        // Returns a structured (result, errorCode, errorMessage) tuple so
                        // wrong_document validation can be surfaced cleanly to the visual.
                        selectionRich:   input => RichDispatch(handler, input, isCreateView: false),
                        createViewRich:  input => RichDispatch(handler, input, isCreateView: true)),
                    port: 27016);
                _pbiSelectListener.Start();
            }

            UpdateConnectionButtonIcon();
            ServiceStateChanged?.Invoke();
        }
    }

    public void StopService()
    {
        _pbiSelectListener?.Stop();
        _pbiSelectListener = null;
        _socketService?.Stop();
        UpdateConnectionButtonIcon();
        ServiceStateChanged?.Invoke();
    }

    /// <summary>
    /// Shared rich-dispatch shim: forwards both Select and CreateView to the
    /// handler's *Rich overload, then unpacks Request.Error into the
    /// structured listener response.
    /// </summary>
    private PbiSelectHttpListener.RichRequestResult RichDispatch(
        PbiActionEventHandler handler,
        PbiSelectHttpListener.RichRequestInput input,
        bool isCreateView)
    {
        if (_uiApplication == null)
            return PbiSelectHttpListener.RichRequestResult.Fail("no_application", "RevitCortex not bound to a UIApplication.");

        var req = isCreateView
            ? handler.DispatchCreateViewRich(input.ElementIds, input.UniqueIds, input.ViewName, input.DocumentTitle)
            : handler.DispatchSelectionRich(input.ElementIds, input.UniqueIds, input.Action, input.DocumentTitle);

        // Request.Error is "code:message"; parse and forward.
        if (!string.IsNullOrEmpty(req.Error))
        {
            var sep = req.Error!.IndexOf(':');
            return sep > 0
                ? PbiSelectHttpListener.RichRequestResult.Fail(req.Error.Substring(0, sep), req.Error.Substring(sep + 1))
                : PbiSelectHttpListener.RichRequestResult.Fail(req.Error, req.Error);
        }
        return PbiSelectHttpListener.RichRequestResult.Ok(req.Result);
    }

    private void UpdateConnectionButtonIcon()
    {
        if (_connectButton == null) return;
        bool active = IsServiceRunning;
        _connectButton.Image = IconFactory.CreateConnectionIcon(16, active);
        _connectButton.LargeImage = IconFactory.CreateConnectionIcon(32, active);
        _connectButton.ToolTip = active
            ? Localization.T("ribbon.connect.tooltip_on", _port)
            : Localization.T("ribbon.connect.tooltip_off");
    }

    /// <summary>
    /// Called when Auto mode is activated or deactivated. Shows a non-modal
    /// floating window while Auto is active and closes it when Auto stops.
    /// Marshals to the UI thread since the activating "Auto" click and the
    /// deactivation paths can originate off the UI thread.
    /// </summary>
    private void OnAutoModeChanged(bool active)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke((System.Action)(() => OnAutoModeChanged(active)));
            return;
        }

        UpdateAutopilotButton();

        if (active)
        {
            var autopilot = _session?.UnattendedMode == true;
            if (_autoModeWindow != null)
            {
                _autoModeWindow.SetMode(autopilot);
                return; // already showing
            }
            try
            {
                var revitHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                _autoModeWindow = new UI.AutoModeWindow(revitHandle);
                _autoModeWindow.SetMode(autopilot);
                _autoModeWindow.StopRequested += OnAutoModeWindowStopRequested;
                _autoModeWindow.Closed += (_, _) => _autoModeWindow = null;
                _autoModeWindow.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Could not show Auto mode window: {ex.Message}");
                _autoModeWindow = null;
            }
        }
        else
        {
            // Auto turned off from somewhere other than the window (e.g. document
            // close). Close without re-triggering the stop callback.
            _autoModeWindow?.CloseFromHost();
            _autoModeWindow = null;
        }
    }

    /// <summary>
    /// Forwards an Auto-mode auto-approval to the floating window. Marshals to
    /// the UI thread because the signal originates on the tool-execution thread.
    /// </summary>
    private void OnAutoModeActivity()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke((System.Action)(() => _autoModeWindow?.RegisterActivity()));
    }

    /// <summary>
    /// The floating window asked to stop (button or X). Turns off Autopilot if
    /// it is on, otherwise plain Auto mode.
    /// </summary>
    private void OnAutoModeWindowStopRequested()
    {
        if (_session != null && _session.UnattendedMode)
        {
            StopAutopilot("log.stop");
            return;
        }
        if (_session != null)
            _session.AutoMode = false;
        ConfirmationHelper.NotifyAutoModeChanged(false);
    }

    /// <summary>
    /// Turns on Autopilot (Auto + Unattended mode). Called by the ribbon toggle
    /// after the user confirms.
    /// </summary>
    public void StartAutopilot(bool allowCriticalScripts)
    {
        if (_session == null) return;
        _session.AutoSave.Reset();
        _session.UnattendedAllowCritical = allowCriticalScripts;
        _session.UnattendedMode = true;
        _session.AutoMode = true;
        UI.UnattendedLog.Separator(Localization.T(allowCriticalScripts ? "log.start_scripts" : "log.start"));
        ConfirmationHelper.NotifyAutoModeChanged(true);
    }

    /// <summary>
    /// Turns Autopilot off from any path (ribbon toggle, floating window,
    /// document close). <paramref name="logKey"/> picks the log message.
    /// </summary>
    public void StopAutopilot(string logKey)
    {
        if (_session == null) return;
        var wasOn = _session.UnattendedMode;
        _session.AutoMode = false;
        _session.UnattendedMode = false;
        _session.UnattendedAllowCritical = false;
        _session.AutoSave.Reset();
        if (wasOn) UI.UnattendedLog.Separator(Localization.T(logKey));
        ConfirmationHelper.NotifyAutoModeChanged(false);
    }

    /// <summary>Grey "Autopilot" when off, orange "Autopilot ON" when on.</summary>
    private void UpdateAutopilotButton()
    {
        if (_autopilotButton == null) return;
        var on = _session?.UnattendedMode == true;
        _autopilotButton.ItemText = Localization.T(on ? "ribbon.autopilot.text_on" : "ribbon.autopilot.text_off");
        _autopilotButton.ToolTip = Localization.T(on ? "ribbon.autopilot.tooltip_on" : "ribbon.autopilot.tooltip_off");
        _autopilotButton.Image = IconFactory.CreateAutopilotIcon(16, on);
        _autopilotButton.LargeImage = IconFactory.CreateAutopilotIcon(32, on);
    }

    /// <summary>
    /// Logs every confirmation resolved without a human and shows it in the
    /// floating window. Only while Autopilot is on, to avoid noise when the
    /// user is present and clicked "Auto".
    /// </summary>
    private void OnAutoDecision(string action, int count, string? description, bool approved)
    {
        if (_session == null || !_session.UnattendedMode) return;
        var detail = string.IsNullOrEmpty(description) ? "" : " — " + UI.UnattendedLog.Shorten(description, 200);
        UI.UnattendedLog.Write(Localization.T(approved ? "log.approved" : "log.declined", action, count, detail));
        SetWindowStatus(Localization.T(approved ? "win.status_approved" : "win.status_declined",
            DateTime.Now.ToString("HH:mm"), action, count));
    }

    private void SetWindowStatus(string text)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke((System.Action)(() => _autoModeWindow?.SetStatus(text)));
    }

    private void CreateRibbonPanel(UIControlledApplication application)
    {
        // Dev builds get a distinct ribbon tab/panel name so a side-by-side
        // prod install never collides ("two addins, same tab name" is a hard
        // Revit conflict, not a cosmetic issue).
        string panelTitle = CortexEnvironment.Current.IsDev ? "RVT Vortex Dev" : "RVT Vortex";
        RibbonPanel panel = application.CreateRibbonPanel(panelTitle);
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;

        static string OneLine(string text) => text.Replace("\r\n", " ");

        // ── Main toggles (large): server and Autopilot ──────────────────
        // Both read grey when off and Claude orange when on.
        var connectBtnData = new PushButtonData(
            "ID_CORTEX_TOGGLE", "Vortex\r\nSwitch",
            assemblyLocation, "RevitCortex.Plugin.Commands.ToggleConnection");
        connectBtnData.ToolTip = Localization.T("ribbon.connect.tooltip_off");
        connectBtnData.Image = IconFactory.CreateConnectionIcon(16, false);
        connectBtnData.LargeImage = IconFactory.CreateConnectionIcon(32, false);
        _connectButton = panel.AddItem(connectBtnData) as Autodesk.Revit.UI.PushButton;

        var autopilotBtnData = new PushButtonData(
            "ID_CORTEX_AUTOPILOT", Localization.T("ribbon.autopilot.text_off"),
            assemblyLocation, "RevitCortex.Plugin.Commands.ToggleAutopilot");
        autopilotBtnData.ToolTip = Localization.T("ribbon.autopilot.tooltip_off");
        autopilotBtnData.LongDescription = Localization.T("ribbon.autopilot.long");
        autopilotBtnData.Image = IconFactory.CreateAutopilotIcon(16, false);
        autopilotBtnData.LargeImage = IconFactory.CreateAutopilotIcon(32, false);
        _autopilotButton = panel.AddItem(autopilotBtnData) as Autodesk.Revit.UI.PushButton;

        panel.AddSeparator();

        // ── Secondary commands (small, stacked) ─────────────────────────
        var settingsBtn = new PushButtonData(
            "ID_CORTEX_SETTINGS", OneLine(Localization.T("ribbon.settings.text")),
            assemblyLocation, "RevitCortex.Plugin.Commands.OpenSettings");
        settingsBtn.ToolTip = Localization.T("ribbon.settings.tooltip");
        settingsBtn.Image = IconFactory.CreateSettingsIcon(16);
        settingsBtn.LargeImage = IconFactory.CreateSettingsIcon(32);

        var licenseBtn = new PushButtonData(
            "ID_CORTEX_LICENSE", OneLine(Localization.T("ribbon.license.text")),
            assemblyLocation, "RevitCortex.Plugin.Commands.OpenLicense");
        licenseBtn.ToolTip = Localization.T("ribbon.license.tooltip");
        licenseBtn.Image = IconFactory.CreateLicenseIcon(16);
        licenseBtn.LargeImage = IconFactory.CreateLicenseIcon(32);

        var powerBiBtn = new PushButtonData(
            "ID_CORTEX_POWERBI", OneLine(Localization.T("ribbon.powerbi.text")),
            assemblyLocation, "RevitCortex.Plugin.Commands.OpenPowerBiExport");
        powerBiBtn.ToolTip = Localization.T("ribbon.powerbi.tooltip");
        powerBiBtn.LongDescription = Localization.T("ribbon.powerbi.long");
        powerBiBtn.Image = IconFactory.CreatePowerBiIcon(16);
        powerBiBtn.LargeImage = IconFactory.CreatePowerBiIcon(32);

        var supportBtn = new PushButtonData(
            "ID_CORTEX_SUPPORT", OneLine(Localization.T("ribbon.support.text")),
            assemblyLocation, "RevitCortex.Plugin.Commands.SendSupportReport");
        supportBtn.ToolTip = Localization.T("ribbon.support.tooltip");
        supportBtn.LongDescription = Localization.T("ribbon.support.long");
        supportBtn.Image = IconFactory.CreateSupportIcon(16);
        supportBtn.LargeImage = IconFactory.CreateSupportIcon(32);

        panel.AddStackedItems(settingsBtn, licenseBtn);
        panel.AddStackedItems(powerBiBtn, supportBtn);

        // Auto mode / Autopilot are stopped via the floating AutoModeWindow
        // shown while active (see OnAutoModeChanged) or the Autopilot toggle.
    }

    private void OnDocumentOpened(object? sender, DocumentOpenedEventArgs args)
    {
        var doc = args.Document;
        if (doc == null) return;

        var locale = LocaleDetector.Detect(doc);
        _router!.OnDocumentChanged(doc, locale);

        // Re-store UIApplication after session reinitialize (needed by send_code_to_revit)
        if (_uiApplication != null)
            _session?.Store.Set("uiApplication", _uiApplication);

        System.Diagnostics.Trace.WriteLine(
            $"[RevitCortex] Document opened. Locale: {locale}, " +
            $"Capabilities: {_router!.GetAvailableToolNames().Count} tools available");
    }

    private void OnDocumentClosing(object? sender, DocumentClosingEventArgs args)
    {
        try
        {
            // Stop the TCP server to prevent stale commands reaching a different document
            if (_socketService != null && _socketService.IsRunning)
            {
                _socketService.Stop();
                UpdateConnectionButtonIcon();
                ServiceStateChanged?.Invoke();
                System.Diagnostics.Trace.WriteLine(
                    "[RevitCortex] Server stopped: document closing");
            }

            // Clear session state (store, capabilities, locale). Reinitialize
            // sets AutoMode=false on the session; notify the UI so the floating
            // Auto mode window closes with the document.
            _session?.Reinitialize(new Core.Discovery.DocumentCapabilities(), "en");
            if (_session != null && _session.UnattendedMode)
                StopAutopilot("log.stop_doc_closed");
            else
                ConfirmationHelper.NotifyAutoModeChanged(false);

            System.Diagnostics.Trace.WriteLine(
                "[RevitCortex] Session reset: document closing");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Error on document closing: {ex.Message}");
        }
    }

    private void OnIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
        if (_uiApplication != null) return;
        _uiApplication = sender as UIApplication;

        // Hook ViewActivated to detect document switches
        if (_uiApplication != null)
        {
            _uiApplication.ViewActivated += OnViewActivated;

            // Store UIApplication in session for send_code_to_revit
            _session?.Store.Set("uiApplication", _uiApplication);

            // If a document is already open, initialize the session now
            var doc = _uiApplication.ActiveUIDocument?.Document;
            if (doc != null && _router != null &&
                _session?.Store.Get<object>("activeDocument") == null)
            {
                var locale = LocaleDetector.Detect(doc);
                _router.OnDocumentChanged(doc, locale);
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Session initialized from Idling: {doc.Title}, locale: {locale}");
            }

            // If the update check already completed before Idling fired, show now.
            if (RevitCortex.Plugin.Updates.UpdateChecker.Latest?.HasUpdate == true)
                ShowUpdateNotification();
        }
    }

    /// <summary>
    /// Unattended mode auto-save. Idling is a valid API context with no open
    /// transaction, so Document.Save() is allowed here. While a save is pending
    /// but throttled, SetRaiseWithoutDelay keeps Idling firing even with no
    /// user input — otherwise the last batch of changes would never be saved.
    /// Each save also leaves a Revit backup copy (.0001.rvt, .0002.rvt…).
    /// </summary>
    private void OnIdlingAutoSave(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
        var session = _session;
        if (session == null || !session.UnattendedMode || !session.AutoSave.HasPending) return;

        var now = DateTime.UtcNow;
        if (!session.AutoSave.ShouldSaveNow(now))
        {
            e.SetRaiseWithoutDelay();
            return;
        }

        var tools = string.Join(", ", session.AutoSave.TakePending(now));
        try
        {
            var doc = (sender as UIApplication)?.ActiveUIDocument?.Document;
            if (doc == null) return;

            if (doc.IsReadOnly)
            {
                UI.UnattendedLog.Write(Localization.T("log.not_saved_readonly"));
                return;
            }
            if (string.IsNullOrEmpty(doc.PathName))
            {
                UI.UnattendedLog.Write(Localization.T("log.not_saved_never"));
                return;
            }
            if (!doc.IsModified) return;

            doc.Save();
            UI.UnattendedLog.Write(Localization.T("log.saved", doc.Title, tools));
            SetWindowStatus(Localization.T("win.status_saved", DateTime.Now.ToString("HH:mm")));
        }
        catch (Exception ex)
        {
            UI.UnattendedLog.Write(Localization.T("log.save_error", tools, ex.Message));
        }
    }

    /// <summary>
    /// Called on a background thread when the update check finds a newer version.
    /// Marshals to the UI thread to show the notification window.
    /// </summary>
    private void OnUpdateAvailable()
    {
        // If Revit isn't idle yet, OnIdling will handle it.
        if (_uiApplication == null) return;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            (System.Action)ShowUpdateNotification);
    }

    private void ShowUpdateNotification()
    {
        if (_updateNotificationShown) return;
        _updateNotificationShown = true;

        try
        {
            var win = new UI.UpdateNotificationWindow();
            win.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not show update notification: {ex.Message}");
        }
    }

    private void OnViewActivated(object? sender, ViewActivatedEventArgs e)
    {
        var doc = e.CurrentActiveView?.Document;
        if (doc == null || _router == null) return;

        // Only update if document changed
        var currentDoc = _session?.Store.Get<object>("activeDocument");
        if (currentDoc != doc)
        {
            var locale = LocaleDetector.Detect(doc);
            _router.OnDocumentChanged(doc, locale);
            // Re-store UIApplication after session reinitialize (needed by send_code_to_revit)
            if (_uiApplication != null)
                _session?.Store.Set("uiApplication", _uiApplication);
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Document switched: {doc.Title}, locale: {locale}");
        }
    }

    private void LoadPort()
    {
        try
        {
            string settingsPath = CortexEnvironment.Current.SettingsFilePath;
            if (System.IO.File.Exists(settingsPath))
            {
                var json = System.IO.File.ReadAllText(settingsPath);
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Newtonsoft.Json.Linq.JObject>(json);
                var port = settings?["Port"]?.ToObject<int>();
                if (port.HasValue && port.Value > 0 && port.Value <= 65535)
                {
                    _port = port.Value;
                    System.Diagnostics.Trace.WriteLine(
                        $"[RevitCortex] Port configured: {_port}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load port setting: {ex.Message}");
        }
    }

    private void LoadReadOnlyMode()
    {
        try
        {
            string settingsPath = CortexEnvironment.Current.SettingsFilePath;
            if (System.IO.File.Exists(settingsPath))
            {
                var json = System.IO.File.ReadAllText(settingsPath);
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Newtonsoft.Json.Linq.JObject>(json);
                var readOnly = settings?["ReadOnlyMode"]?.ToObject<bool>() ?? false;
                _router!.ReadOnlyMode = readOnly;
                if (readOnly)
                    System.Diagnostics.Trace.WriteLine("[RevitCortex] Read-only mode is ON");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load read-only setting: {ex.Message}");
        }
    }

    private void LoadDisabledTools()
    {
        try
        {
            string settingsPath = CortexEnvironment.Current.SettingsFilePath;
            if (System.IO.File.Exists(settingsPath))
            {
                var json = System.IO.File.ReadAllText(settingsPath);
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Newtonsoft.Json.Linq.JObject>(json);
                var disabled = settings?["DisabledTools"]?
                    .ToObject<string[]>() ?? Array.Empty<string>();
                _router!.SetDisabledTools(disabled);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load disabled tools: {ex.Message}");
        }
    }

    private static void CleanupTempScripts()
    {
        var scriptsFolder = CortexEnvironment.Current.ScriptsFolder;
        if (!System.IO.Directory.Exists(scriptsFolder)) return;
        foreach (var file in System.IO.Directory.GetFiles(scriptsFolder, "*.cs"))
        {
            try
            {
                using var reader = new System.IO.StreamReader(file);
                var firstLine = reader.ReadLine() ?? "";
                if (firstLine.TrimStart().StartsWith("// TEMP", StringComparison.OrdinalIgnoreCase))
                    System.IO.File.Delete(file);
            }
            catch { }
        }
    }

    private Assembly? LoadToolsAssembly()
    {
        try
        {
            var pluginDir = System.IO.Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location)!;
            var toolsPath = System.IO.Path.Combine(pluginDir, "RevitCortex.Tools.dll");
            return Assembly.LoadFrom(toolsPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load Tools assembly: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves Roslyn (Microsoft.CodeAnalysis*) and its System.Collections.Immutable /
    /// System.Reflection.Metadata 8.0 dependencies from the plugin's own folder. Revit's
    /// shared AssemblyLoadContext does not probe our folder for these, so without this a
    /// sibling add-in's older copy can win the bind (or the 8.0 deps go unfound), which
    /// breaks send_code_to_revit's Roslyn compiler on Revit 2025+.
    /// Scoped to the Roslyn dependency graph so it never hijacks Revit or other add-ins.
    /// </summary>
    private static Assembly? ResolveBundledDependency(object? sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name).Name;
        if (string.IsNullOrEmpty(requested))
            return null;

        bool wanted = requested.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
            || requested == "System.Collections.Immutable"
            || requested == "System.Reflection.Metadata";
        if (!wanted)
            return null;

        try
        {
            var dir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dir == null)
                return null;
            var candidate = System.IO.Path.Combine(dir, requested + ".dll");
            return System.IO.File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
        catch
        {
            return null;
        }
    }
}
