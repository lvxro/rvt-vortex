using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using Xunit;

namespace RevitCortex.Tests.Router;

/// <summary>
/// In unattended mode a declined operation must tell the AI to keep going,
/// not to stop and ask an absent user.
/// </summary>
public class CortexRouterUnattendedTests
{
    private sealed class DecliningTool : ICortexTool
    {
        public string Name => "fake_delete";
        public string Category => "Test";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "Asks for confirmation and gives up when declined.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            if (!session.RequestConfirmation("delete", 3, critical: true))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled,
                    "Operation cancelled by user",
                    suggestion: "The user declined the confirmation dialog. Ask if they want to retry.");
            return CortexResult<object>.Ok(new { deleted = 3 });
        }
    }

    private static CortexRouter CreateRouter(CortexSession session)
    {
        var router = new CortexRouter(session, new FakeAnalyzer());
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, ICortexTool>)field.GetValue(router)!;
        var tool = new DecliningTool();
        tools[tool.Name] = tool;
        return router;
    }

    [Fact]
    public void Unattended_CancelledResult_TellsAiToContinue()
    {
        var session = new CortexSession(new SessionStore()) { UnattendedMode = true, AutoMode = true };
        var router = CreateRouter(session);

        var result = router.Route("fake_delete", new JObject());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(CortexRouter.UnattendedCancelledSuggestion, result.Error.Suggestion);
        Assert.Contains("unattended", result.Error.Message);
    }

    [Fact]
    public void Unattended_SuccessfulWrite_MarksAutoSave()
    {
        var session = new CortexSession(new SessionStore())
            { UnattendedMode = true, AutoMode = true, UnattendedAllowCritical = true };
        var router = CreateRouter(session);

        var result = router.Route("fake_delete", new JObject());

        Assert.True(result.Success);
        Assert.True(session.AutoSave.HasPending);
    }

    [Fact]
    public void Unattended_FailedWrite_DoesNotMarkAutoSave()
    {
        var session = new CortexSession(new SessionStore()) { UnattendedMode = true, AutoMode = true };
        var router = CreateRouter(session);

        router.Route("fake_delete", new JObject());

        Assert.False(session.AutoSave.HasPending);
    }

    [Fact]
    public void Attended_SuccessfulWrite_DoesNotMarkAutoSave()
    {
        var session = new CortexSession(new SessionStore());
        session.CriticalConfirmAction = (_, _, _) => true;
        var router = CreateRouter(session);

        Assert.True(router.Route("fake_delete", new JObject()).Success);
        Assert.False(session.AutoSave.HasPending);
    }

    [Fact]
    public void Attended_CancelledResult_KeepsOriginalSuggestion()
    {
        var session = new CortexSession(new SessionStore());
        session.CriticalConfirmAction = (_, _, _) => false;
        var router = CreateRouter(session);

        var result = router.Route("fake_delete", new JObject());

        Assert.False(result.Success);
        Assert.Contains("Ask if they want to retry", result.Error!.Suggestion);
    }
}
