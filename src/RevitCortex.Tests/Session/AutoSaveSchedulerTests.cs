using System;
using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Session;

public class AutoSaveSchedulerTests
{
    private static readonly DateTime T0 = new DateTime(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NothingPending_NeverSaves()
    {
        var s = new AutoSaveScheduler(TimeSpan.FromSeconds(60));
        Assert.False(s.HasPending);
        Assert.False(s.ShouldSaveNow(T0));
    }

    [Fact]
    public void FirstChange_SavesImmediately()
    {
        var s = new AutoSaveScheduler(TimeSpan.FromSeconds(60));
        s.MarkChanged("create_wall");
        Assert.True(s.ShouldSaveNow(T0));
    }

    [Fact]
    public void ChangesInsideWindow_WaitThenSaveTogether()
    {
        var s = new AutoSaveScheduler(TimeSpan.FromSeconds(60));
        s.MarkChanged("create_wall");
        s.TakePending(T0);

        s.MarkChanged("create_door");
        s.MarkChanged("tag_rooms");
        s.MarkChanged("create_door");
        Assert.False(s.ShouldSaveNow(T0.AddSeconds(30)));
        Assert.True(s.HasPending);

        Assert.True(s.ShouldSaveNow(T0.AddSeconds(60)));
        var taken = s.TakePending(T0.AddSeconds(60));
        Assert.Equal(new[] { "create_door", "tag_rooms" }, taken);
        Assert.False(s.HasPending);
    }

    [Fact]
    public void Reset_DropsPendingAndThrottle()
    {
        var s = new AutoSaveScheduler(TimeSpan.FromSeconds(60));
        s.MarkChanged("create_wall");
        s.TakePending(T0);
        s.MarkChanged("create_door");

        s.Reset();

        Assert.False(s.HasPending);
        s.MarkChanged("create_floor");
        Assert.True(s.ShouldSaveNow(T0.AddSeconds(1)));
    }

    [Fact]
    public void Session_ResetApproveAll_ClearsPendingSaves()
    {
        var session = new CortexSession(new SessionStore());
        session.AutoSave.MarkChanged("create_wall");

        session.ResetApproveAll();

        Assert.False(session.AutoSave.HasPending);
    }
}
