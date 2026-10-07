using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Session;

public class AutopilotActivityTests
{
    private static readonly DateTime T0 = new DateTime(2026, 10, 7, 14, 0, 0);

    [Fact]
    public void Snapshot_BeforeStart_IsEmpty()
    {
        var snapshot = new AutopilotActivity().Snapshot();

        Assert.Null(snapshot.StartedAt);
        Assert.False(snapshot.HasAnything);
        Assert.Empty(snapshot.RecentEvents);
        Assert.Empty(snapshot.DeclinedEvents);
    }

    [Fact]
    public void RecordDecision_CountsApprovedAndDeclinedSeparately()
    {
        var activity = new AutopilotActivity();
        activity.Start(T0);

        activity.RecordDecision(true, "rename (12)", T0.AddMinutes(1));
        activity.RecordDecision(true, "modify parameters (48)", T0.AddMinutes(2));
        activity.RecordDecision(false, "execute C# script (1)", T0.AddMinutes(3));

        var snapshot = activity.Snapshot();
        Assert.Equal(2, snapshot.Approved);
        Assert.Equal(1, snapshot.Declined);
        Assert.True(snapshot.HasAnything);
        Assert.Equal(T0.AddMinutes(3), snapshot.LastActivityAt);

        var declined = Assert.Single(snapshot.DeclinedEvents);
        Assert.Equal("execute C# script (1)", declined.Text);
        Assert.Equal(AutopilotEventKind.Declined, declined.Kind);
    }

    [Fact]
    public void RecentEvents_AreNewestFirst_AndCappedAtCapacity()
    {
        var activity = new AutopilotActivity();
        activity.Start(T0);

        for (int i = 1; i <= AutopilotActivity.RecentCapacity + 3; i++)
            activity.RecordDecision(true, "op " + i, T0.AddMinutes(i));

        var snapshot = activity.Snapshot();
        Assert.Equal(AutopilotActivity.RecentCapacity, snapshot.RecentEvents.Count);
        Assert.Equal("op " + (AutopilotActivity.RecentCapacity + 3), snapshot.RecentEvents[0].Text);
        Assert.Equal("op 4", snapshot.RecentEvents[snapshot.RecentEvents.Count - 1].Text);
        // The counter keeps the real total even though the feed is capped.
        Assert.Equal(AutopilotActivity.RecentCapacity + 3, snapshot.Approved);
    }

    [Fact]
    public void DeclinedEvents_AreOldestFirst_AndCapped()
    {
        var activity = new AutopilotActivity();
        activity.Start(T0);

        for (int i = 1; i <= AutopilotActivity.DeclinedCapacity + 5; i++)
            activity.RecordDecision(false, "step " + i, T0.AddSeconds(i));

        var snapshot = activity.Snapshot();
        Assert.Equal(AutopilotActivity.DeclinedCapacity, snapshot.DeclinedEvents.Count);
        Assert.Equal("step 1", snapshot.DeclinedEvents[0].Text);
        Assert.Equal(AutopilotActivity.DeclinedCapacity + 5, snapshot.Declined);
    }

    [Fact]
    public void RecordDialogClosed_And_RecordSaved_FeedTheSnapshot()
    {
        var activity = new AutopilotActivity();
        activity.Start(T0);

        activity.RecordDialogClosed("closed with Cancel", T0.AddMinutes(1));
        activity.RecordSaved("model saved", T0.AddMinutes(2));

        var snapshot = activity.Snapshot();
        Assert.Equal(1, snapshot.DialogsClosed);
        Assert.Equal(T0.AddMinutes(2), snapshot.LastSavedAt);
        Assert.Equal(AutopilotEventKind.Saved, snapshot.RecentEvents[0].Kind);
        Assert.Equal(AutopilotEventKind.DialogClosed, snapshot.RecentEvents[1].Kind);
        Assert.True(snapshot.HasAnything);
    }

    [Fact]
    public void RecordToolCall_IsAHeartbeat_NotAnEvent()
    {
        var activity = new AutopilotActivity();
        activity.Start(T0);

        activity.RecordToolCall(T0.AddSeconds(30));

        var snapshot = activity.Snapshot();
        Assert.Equal(T0.AddSeconds(30), snapshot.LastToolCallAt);
        Assert.Equal(T0.AddSeconds(30), snapshot.LastActivityAt);
        Assert.Empty(snapshot.RecentEvents);
        Assert.False(snapshot.HasAnything);
    }

    [Fact]
    public void Start_ClearsThePreviousRun()
    {
        var activity = new AutopilotActivity();
        activity.Start(T0);
        activity.RecordDecision(false, "old", T0.AddMinutes(1));
        activity.RecordSaved("saved", T0.AddMinutes(2));

        activity.Start(T0.AddHours(1));

        var snapshot = activity.Snapshot();
        Assert.Equal(T0.AddHours(1), snapshot.StartedAt);
        Assert.Equal(0, snapshot.Declined);
        Assert.Null(snapshot.LastSavedAt);
        Assert.Empty(snapshot.RecentEvents);
        Assert.Empty(snapshot.DeclinedEvents);
    }
}
