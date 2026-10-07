using System;
using System.IO;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Session;

/// <summary>
/// Characterization tests for destructive-operation confirmation gates.
/// </summary>
public class CortexSessionConfirmationTests
{
    private static CortexSession NewSession()
    {
        return new CortexSession(new SessionStore());
    }

    private static string ReadAutoModeWindowSource()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Plugin", "UI", "AutoModeWindow.xaml.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void RequestConfirmation_WhenAutoModeOn_AutoApprovesWithoutInvokingCallback()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ConfirmAction = (_, _, _) => { callbackInvoked = true; return false; };
        session.AutoMode = true;

        var result = session.RequestConfirmation("delete", 5);

        Assert.True(result);
        Assert.False(callbackInvoked);
    }

    [Fact]
    public void RequestConfirmation_WhenAutoModeOff_InvokesCallback()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ConfirmAction = (_, _, _) => { callbackInvoked = true; return true; };

        session.RequestConfirmation("delete", 5);

        Assert.True(callbackInvoked);
    }

    [Fact]
    public void AutoMode_HasNoTimeout_StaysTrueWhenSet()
    {
        var session = NewSession();
        session.AutoMode = true;

        Assert.True(session.AutoMode);
    }

    [Fact]
    public void RequestConfirmation_WhenAutoModeOn_FiresAutoModeActivity()
    {
        var session = NewSession();
        session.AutoMode = true;
        var activityCount = 0;
        session.AutoModeActivity += () => activityCount++;

        session.RequestConfirmation("delete", 5);
        session.RequestConfirmation("delete", 3);

        Assert.Equal(2, activityCount);
    }

    [Fact]
    public void RequestConfirmation_WhenAutoModeOff_DoesNotFireAutoModeActivity()
    {
        var session = NewSession();
        var activityFired = false;
        session.AutoModeActivity += () => activityFired = true;
        session.ConfirmAction = (_, _, _) => true;

        session.RequestConfirmation("delete", 5);

        Assert.False(activityFired);
    }

    [Fact]
    public void AutoModeWindow_HasNoInactivityTimer()
    {
        var source = ReadAutoModeWindowSource();

        Assert.DoesNotContain("InactivitySeconds", source);
        Assert.DoesNotContain("OnInactivityElapsed", source);

        // The pill redraws itself once a second (elapsed times, save state,
        // warnings). That is the only timer allowed in the window, and all it
        // may do is redraw: nothing on a timer may stop Auto mode.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, "new DispatcherTimer"));
        Assert.Contains("_refreshTimer.Tick += (_, _) => RefreshFromProvider();", source);
    }

    [Fact]
    public void Reinitialize_ResetsAutoMode()
    {
        var session = NewSession();
        session.AutoMode = true;

        session.Reinitialize(new DocumentCapabilities(), "en");

        Assert.False(session.AutoMode);
    }

    [Fact]
    public void ResetApproveAll_ResetsBothApproveAllAndAutoMode()
    {
        var session = NewSession();
        session.ApproveAll = true;
        session.AutoMode = true;

        session.ResetApproveAll();

        Assert.False(session.ApproveAll);
        Assert.False(session.AutoMode);
    }

    [Fact]
    public void RequestConfirmation_WhenApproveAllOn_AutoApprovesWithoutCallback()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ConfirmAction = (_, _, _) => { callbackInvoked = true; return false; };
        session.ApproveAll = true;

        var result = session.RequestConfirmation("purge", 3);

        Assert.True(result);
        Assert.False(callbackInvoked);
    }

    [Fact]
    public void RequestConfirmation_NullCallbackResult_TreatedAsYesToAll_SetsApproveAll()
    {
        var session = NewSession();
        session.ConfirmAction = (_, _, _) => null;

        var result = session.RequestConfirmation("rename", 2);

        Assert.True(result);
        Assert.True(session.ApproveAll);
    }

    [Fact]
    public void RequestConfirmation_ZeroElements_ReturnsTrueWithoutCallback()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ConfirmAction = (_, _, _) => { callbackInvoked = true; return false; };

        var result = session.RequestConfirmation("delete", 0);

        Assert.True(result);
        Assert.False(callbackInvoked);
    }

    [Fact]
    public void RequestConfirmation_NoCallbackSet_ProceedsWithoutBlocking()
    {
        var session = NewSession();

        var result = session.RequestConfirmation("delete", 10);

        Assert.True(result);
    }

    [Fact]
    public void RequestConfirmation_CallbackReturnsFalse_Cancels()
    {
        var session = NewSession();
        session.ConfirmAction = (_, _, _) => false;

        var result = session.RequestConfirmation("delete", 10);

        Assert.False(result);
    }

    [Fact]
    public void RequestConfirmation_CriticalWithoutCallback_FailsClosed()
    {
        var session = NewSession();

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
    }

    [Fact]
    public void RequestConfirmation_CriticalIgnoresApproveAll()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ApproveAll = true;
        session.ConfirmAction = (_, _, _) =>
        {
            throw new InvalidOperationException("Normal callback must not handle critical confirmations.");
        };
        session.CriticalConfirmAction = (_, _, _) =>
        {
            callbackInvoked = true;
            return false;
        };

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void RequestConfirmation_CriticalIgnoresAutoMode()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.AutoMode = true;
        session.ConfirmAction = (_, _, _) =>
        {
            throw new InvalidOperationException("Normal callback must not handle critical confirmations.");
        };
        session.CriticalConfirmAction = (_, _, _) =>
        {
            callbackInvoked = true;
            return false;
        };

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void RequestConfirmation_CriticalNullCallbackResult_CancelsAndDoesNotArmApproveAll()
    {
        var session = NewSession();
        session.CriticalConfirmAction = (_, _, _) => null;

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
        Assert.False(session.ApproveAll);
    }

    [Fact]
    public void RequestConfirmation_CriticalIgnoresNormalCallback()
    {
        var session = NewSession();
        session.ConfirmAction = (_, _, _) => true;

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
    }

    [Fact]
    public void RequestConfirmation_CriticalAllowsExplicitCriticalYes()
    {
        var session = NewSession();
        session.CriticalConfirmAction = (_, _, _) => true;

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.True(result);
    }

    // ---- Unattended ("leave it working") mode ----

    [Fact]
    public void Unattended_CriticalIsDeclinedWithoutShowingDialog()
    {
        var session = NewSession();
        session.UnattendedMode = true;
        session.AutoMode = true;
        session.CriticalConfirmAction = (_, _, _) =>
            throw new InvalidOperationException("No dialog may open while unattended.");

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
    }

    [Fact]
    public void Unattended_NonCriticalIsAutoApproved()
    {
        var session = NewSession();
        session.UnattendedMode = true;
        session.AutoMode = true;
        session.ConfirmAction = (_, _, _) =>
            throw new InvalidOperationException("No dialog may open while unattended.");

        Assert.True(session.RequestConfirmation("delete", 5));
    }

    [Fact]
    public void Unattended_SurvivesReinitialize_AndThenFailsClosedInsteadOfBlocking()
    {
        var session = NewSession();
        session.UnattendedMode = true;
        session.AutoMode = true;
        session.ConfirmAction = (_, _, _) =>
            throw new InvalidOperationException("No dialog may open while unattended.");

        session.Reinitialize(new DocumentCapabilities(), "en");

        Assert.False(session.AutoMode);
        Assert.True(session.UnattendedMode);
        Assert.False(session.RequestConfirmation("delete", 5));
    }

    [Fact]
    public void Unattended_ClearedByResetApproveAll()
    {
        var session = NewSession();
        session.UnattendedMode = true;
        session.AutoMode = true;

        session.ResetApproveAll();

        Assert.False(session.UnattendedMode);
        Assert.False(session.AutoMode);
    }

    [Fact]
    public void AutoDecision_ReportsApprovalsAndDeclines()
    {
        var session = NewSession();
        var decisions = new System.Collections.Generic.List<(string, bool)>();
        session.AutoDecision += (action, _, _, approved) => decisions.Add((action, approved));
        session.UnattendedMode = true;
        session.AutoMode = true;

        session.RequestConfirmation("delete", 3);
        session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.Equal(2, decisions.Count);
        Assert.Equal(("delete", true), decisions[0]);
        Assert.Equal(("execute C# script", false), decisions[1]);
    }

    [Fact]
    public void Unattended_WithAllowCritical_ApprovesCriticalWithoutDialog()
    {
        var session = NewSession();
        session.UnattendedMode = true;
        session.AutoMode = true;
        session.UnattendedAllowCritical = true;
        session.CriticalConfirmAction = (_, _, _) =>
            throw new InvalidOperationException("No dialog may open while unattended.");

        Assert.True(session.RequestConfirmation("execute C# script", 1, critical: true));
    }

    [Fact]
    public void AllowCritical_IsIgnoredOutsideUnattended()
    {
        var session = NewSession();
        session.UnattendedAllowCritical = true;
        session.CriticalConfirmAction = (_, _, _) => false;

        Assert.False(session.RequestConfirmation("execute C# script", 1, critical: true));
    }

    [Fact]
    public void ResetApproveAll_ClearsAllowCritical()
    {
        var session = NewSession();
        session.UnattendedMode = true;
        session.UnattendedAllowCritical = true;

        session.ResetApproveAll();

        Assert.False(session.UnattendedAllowCritical);
    }

    [Fact]
    public void NotUnattended_CriticalStillAsksTheUser()
    {
        var session = NewSession();
        var asked = false;
        session.AutoMode = true;
        session.CriticalConfirmAction = (_, _, _) => { asked = true; return true; };

        Assert.True(session.RequestConfirmation("execute C# script", 1, critical: true));
        Assert.True(asked);
    }
}
