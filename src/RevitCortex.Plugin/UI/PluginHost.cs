using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// The only door from the WPF windows to <see cref="RevitCortexApp"/>.
///
/// RevitCortexApp implements a Revit interface, so merely touching the type
/// loads RevitAPIUI.dll. Windows that reference it directly cannot even be
/// constructed where Revit is absent (unit tests, the UI preview renderer in
/// CI). Every access here sits in its own non-inlined method called inside a
/// try/catch: without Revit the call fails when that method is JIT-compiled,
/// the catch turns it into a default value, and the window still opens.
///
/// Inside Revit this is a thin pass-through with no behavior of its own.
/// </summary>
internal static class PluginHost
{
    public static bool IsServiceRunning
    {
        get { try { return ReadIsServiceRunning(); } catch { return false; } }
    }

    /// <summary>Configured port, or null when the plugin is not loaded.</summary>
    public static int? Port
    {
        get { try { return ReadPort(); } catch { return null; } }
    }

    public static int ToolCount
    {
        get { try { return ReadToolCount(); } catch { return 0; } }
    }

    /// <summary>Starts the server. False when the plugin is not loaded.</summary>
    public static bool StartService()
    {
        try { DoStartService(); return true; } catch { return false; }
    }

    public static bool StopService()
    {
        try { DoStopService(); return true; } catch { return false; }
    }

    public static void SetReadOnlyMode(bool readOnly)
    {
        try { DoSetReadOnlyMode(readOnly); } catch { /* plugin not loaded */ }
    }

    public static void SetDisabledTools(IEnumerable<string> toolNames)
    {
        try { DoSetDisabledTools(toolNames); } catch { /* plugin not loaded */ }
    }

    /// <summary>Registered tools, or null when the plugin is not loaded.</summary>
    public static IReadOnlyList<(string Name, string Category, string Description, bool IsEnabled)>? GetAllToolInfo()
    {
        try { return ReadAllToolInfo(); } catch { return null; }
    }

    public static void SubscribeServiceState(Action handler)
    {
        try { DoSubscribe(handler); } catch { /* plugin not loaded */ }
    }

    public static void UnsubscribeServiceState(Action handler)
    {
        try { DoUnsubscribe(handler); } catch { /* plugin not loaded */ }
    }

    // ── Revit-touching bodies, one per method so each JITs on its own ──────

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ReadIsServiceRunning() => RevitCortexApp.Instance?.IsServiceRunning ?? false;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int? ReadPort() => RevitCortexApp.Instance?.Port;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadToolCount() => RevitCortexApp.Instance?.Router?.TotalToolCount ?? 0;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DoStartService()
    {
        var app = RevitCortexApp.Instance;
        if (app == null) throw new InvalidOperationException("Plugin not loaded");
        app.StartServiceFromUi();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DoStopService()
    {
        var app = RevitCortexApp.Instance;
        if (app == null) throw new InvalidOperationException("Plugin not loaded");
        app.StopService();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DoSetReadOnlyMode(bool readOnly)
    {
        var router = RevitCortexApp.Instance?.Router;
        if (router != null) router.ReadOnlyMode = readOnly;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DoSetDisabledTools(IEnumerable<string> toolNames)
        => RevitCortexApp.Instance?.Router?.SetDisabledTools(toolNames);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<(string Name, string Category, string Description, bool IsEnabled)>? ReadAllToolInfo()
        => RevitCortexApp.Instance?.Router?.GetAllToolInfo();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DoSubscribe(Action handler)
    {
        var app = RevitCortexApp.Instance;
        if (app != null) app.ServiceStateChanged += handler;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DoUnsubscribe(Action handler)
    {
        var app = RevitCortexApp.Instance;
        if (app != null) app.ServiceStateChanged -= handler;
    }
}
