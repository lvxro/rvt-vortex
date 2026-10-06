using System;
using System.Collections.Generic;

namespace RevitCortex.Core.Session;

/// <summary>
/// Decides when to save the model while Unattended mode is on. Revit-agnostic
/// so it can be unit tested: the router reports successful write tools via
/// <see cref="MarkChanged"/>, and the plugin's Idling handler asks
/// <see cref="ShouldSaveNow"/> and performs the actual save.
///
/// Saves are throttled to at most one per <see cref="MinInterval"/> so a burst
/// of small edits on a large model doesn't spend more time saving than working.
/// Changes made during the throttle window are not lost: they stay pending and
/// are saved by the next eligible save.
/// </summary>
public sealed class AutoSaveScheduler
{
    private readonly object _lock = new object();
    private readonly List<string> _pendingTools = new List<string>();
    private DateTime _lastSaveUtc = DateTime.MinValue;

    public AutoSaveScheduler() : this(TimeSpan.FromSeconds(60)) { }

    public AutoSaveScheduler(TimeSpan minInterval)
    {
        MinInterval = minInterval;
    }

    public TimeSpan MinInterval { get; }

    public bool HasPending
    {
        get { lock (_lock) { return _pendingTools.Count > 0; } }
    }

    /// <summary>Records that a tool changed the model and a save is due.</summary>
    public void MarkChanged(string toolName)
    {
        lock (_lock)
        {
            if (!_pendingTools.Contains(toolName)) _pendingTools.Add(toolName);
        }
    }

    /// <summary>True when there are pending changes and the throttle window has passed.</summary>
    public bool ShouldSaveNow(DateTime nowUtc)
    {
        lock (_lock)
        {
            return _pendingTools.Count > 0 && nowUtc - _lastSaveUtc >= MinInterval;
        }
    }

    /// <summary>
    /// Clears the pending list and restarts the throttle window. Returns the
    /// tools whose changes this save covered (for the log). Call it after a
    /// save attempt even if it failed, so a persistent error is retried once
    /// per interval instead of on every Idling tick.
    /// </summary>
    public IReadOnlyList<string> TakePending(DateTime nowUtc)
    {
        lock (_lock)
        {
            var taken = _pendingTools.ToArray();
            _pendingTools.Clear();
            _lastSaveUtc = nowUtc;
            return taken;
        }
    }

    /// <summary>Drops pending work without saving (mode stopped, document closed).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _pendingTools.Clear();
            _lastSaveUtc = DateTime.MinValue;
        }
    }
}
