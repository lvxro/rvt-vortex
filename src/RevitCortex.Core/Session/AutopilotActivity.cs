using System;
using System.Collections.Generic;

namespace RevitCortex.Core.Session;

/// <summary>What happened without the user while Autopilot was on.</summary>
public enum AutopilotEventKind
{
    /// <summary>A confirmation was approved automatically.</summary>
    Approved,
    /// <summary>A confirmation was declined because nobody was there to answer it.</summary>
    Declined,
    /// <summary>One of Revit's own pop-ups was closed.</summary>
    DialogClosed,
    /// <summary>The model was saved.</summary>
    Saved,
}

/// <summary>One line of the Autopilot activity feed. Times are local.</summary>
public sealed class AutopilotEvent
{
    public AutopilotEvent(DateTime time, AutopilotEventKind kind, string text)
    {
        Time = time;
        Kind = kind;
        Text = text ?? "";
    }

    public DateTime Time { get; }
    public AutopilotEventKind Kind { get; }
    public string Text { get; }
}

/// <summary>
/// Immutable copy of the counters at one moment, safe to hand to the UI.
/// </summary>
public sealed class AutopilotSnapshot
{
    public AutopilotSnapshot(DateTime? startedAt, int approved, int declined, int dialogsClosed,
        DateTime? lastSavedAt, DateTime? lastActivityAt, DateTime? lastToolCallAt,
        IReadOnlyList<AutopilotEvent> recentEvents, IReadOnlyList<AutopilotEvent> declinedEvents)
    {
        StartedAt = startedAt;
        Approved = approved;
        Declined = declined;
        DialogsClosed = dialogsClosed;
        LastSavedAt = lastSavedAt;
        LastActivityAt = lastActivityAt;
        LastToolCallAt = lastToolCallAt;
        RecentEvents = recentEvents;
        DeclinedEvents = declinedEvents;
    }

    /// <summary>When Autopilot was turned on; null if it never was.</summary>
    public DateTime? StartedAt { get; }
    public int Approved { get; }
    public int Declined { get; }
    public int DialogsClosed { get; }
    public DateTime? LastSavedAt { get; }
    /// <summary>Latest recorded event or tool call.</summary>
    public DateTime? LastActivityAt { get; }
    /// <summary>Latest tool call from the AI, whether or not it needed a confirmation.</summary>
    public DateTime? LastToolCallAt { get; }
    /// <summary>Newest first, at most <see cref="AutopilotActivity.RecentCapacity"/>.</summary>
    public IReadOnlyList<AutopilotEvent> RecentEvents { get; }
    /// <summary>Oldest first: the steps left for the user to confirm.</summary>
    public IReadOnlyList<AutopilotEvent> DeclinedEvents { get; }

    /// <summary>True when there is anything worth summarizing.</summary>
    public bool HasAnything => Approved > 0 || Declined > 0 || DialogsClosed > 0 || LastSavedAt.HasValue;

    public static AutopilotSnapshot Empty { get; } = new AutopilotSnapshot(
        null, 0, 0, 0, null, null, null, new AutopilotEvent[0], new AutopilotEvent[0]);
}

/// <summary>
/// Counts what Autopilot decided on its own, for the status pill, its activity
/// panel and the summary shown when Autopilot stops. Revit-agnostic and
/// thread-safe: decisions arrive on the tool-execution thread, dismissed
/// dialogs and saves on the UI thread, and the UI reads snapshots on a timer.
/// The caller supplies the clock, so behavior is testable.
/// </summary>
public sealed class AutopilotActivity
{
    /// <summary>How many events the activity panel shows.</summary>
    public const int RecentCapacity = 5;

    /// <summary>Cap on remembered declined steps (the summary lists them).</summary>
    public const int DeclinedCapacity = 20;

    private readonly object _lock = new object();
    private readonly List<AutopilotEvent> _recent = new List<AutopilotEvent>();
    private readonly List<AutopilotEvent> _declinedEvents = new List<AutopilotEvent>();
    private DateTime? _startedAt;
    private DateTime? _lastSavedAt;
    private DateTime? _lastActivityAt;
    private DateTime? _lastToolCallAt;
    private int _approved;
    private int _declined;
    private int _dialogsClosed;

    /// <summary>Clears everything and starts a new Autopilot run.</summary>
    public void Start(DateTime now)
    {
        lock (_lock)
        {
            _recent.Clear();
            _declinedEvents.Clear();
            _approved = 0;
            _declined = 0;
            _dialogsClosed = 0;
            _lastSavedAt = null;
            _lastActivityAt = null;
            _lastToolCallAt = null;
            _startedAt = now;
        }
    }

    /// <summary>A confirmation resolved without the user.</summary>
    public void RecordDecision(bool approved, string text, DateTime now)
    {
        lock (_lock)
        {
            var e = new AutopilotEvent(now, approved ? AutopilotEventKind.Approved : AutopilotEventKind.Declined, text);
            if (approved)
            {
                _approved++;
            }
            else
            {
                _declined++;
                if (_declinedEvents.Count < DeclinedCapacity) _declinedEvents.Add(e);
            }
            Push(e);
        }
    }

    /// <summary>One of Revit's own pop-ups was dismissed.</summary>
    public void RecordDialogClosed(string text, DateTime now)
    {
        lock (_lock)
        {
            _dialogsClosed++;
            Push(new AutopilotEvent(now, AutopilotEventKind.DialogClosed, text));
        }
    }

    /// <summary>The model was saved.</summary>
    public void RecordSaved(string text, DateTime now)
    {
        lock (_lock)
        {
            _lastSavedAt = now;
            Push(new AutopilotEvent(now, AutopilotEventKind.Saved, text));
        }
    }

    /// <summary>
    /// The AI called a tool. Not an event in the feed (most calls are reads),
    /// only a heartbeat that tells the pill whether the AI is still working.
    /// </summary>
    public void RecordToolCall(DateTime now)
    {
        lock (_lock)
        {
            _lastToolCallAt = now;
            _lastActivityAt = now;
        }
    }

    public AutopilotSnapshot Snapshot()
    {
        lock (_lock)
        {
            var recent = new AutopilotEvent[_recent.Count];
            // Stored oldest first; hand out newest first.
            for (int i = 0; i < _recent.Count; i++)
                recent[i] = _recent[_recent.Count - 1 - i];

            return new AutopilotSnapshot(_startedAt, _approved, _declined, _dialogsClosed,
                _lastSavedAt, _lastActivityAt, _lastToolCallAt,
                recent, _declinedEvents.ToArray());
        }
    }

    // Caller holds _lock.
    private void Push(AutopilotEvent e)
    {
        _recent.Add(e);
        if (_recent.Count > RecentCapacity) _recent.RemoveAt(0);
        _lastActivityAt = e.Time;
    }
}
