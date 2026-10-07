using System;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Everything the Autopilot status pill needs to draw itself at one moment.
/// Built by RevitCortexApp from the session and handed to
/// <see cref="AutoModeWindow.Render"/>; plain data, so the window can also be
/// rendered with made-up states (tests, the UI preview in CI).
/// </summary>
public sealed class AutopilotPillState
{
    /// <summary>True for Autopilot; false for plain Auto mode (user present).</summary>
    public bool Autopilot { get; set; }

    /// <summary>The user ticked "Also allow C# scripts" when starting.</summary>
    public bool ScriptsAllowed { get; set; }

    /// <summary>
    /// Another document became active: Autopilot is still on but now declines
    /// everything that needs a confirmation, so nothing is edited in a model
    /// the user did not pick.
    /// </summary>
    public bool ApprovalsPaused { get; set; }

    /// <summary>The server (Vortex Switch) is off, so the AI cannot connect.</summary>
    public bool ServerOff { get; set; }

    /// <summary>The model has changes that the next auto-save will write.</summary>
    public bool SavePending { get; set; }

    public AutopilotSnapshot Activity { get; set; } = AutopilotSnapshot.Empty;

    /// <summary>"Now" for elapsed-time texts; settable so renders are repeatable.</summary>
    public DateTime Now { get; set; } = DateTime.Now;
}
