using CodeAlta.Agent;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Orchestration.Runtime.Skills;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class WorkThreadSkillActivationPlannerTests
{
    [TestMethod]
    public void Plan_ActivatesForReadyIdleCodeAltaManagedThread()
    {
        var decision = new WorkThreadSkillActivationPlanner().Plan(
            CreateThread("local"),
            isModelProviderReady: true,
            isThreadBusy: false);

        Assert.AreEqual(WorkThreadSkillActivationDecisionKind.Activate, decision.Kind);
    }

    [TestMethod]
    public void Plan_RejectsMissingThread()
    {
        var decision = new WorkThreadSkillActivationPlanner().Plan(
            thread: null,
            isModelProviderReady: true,
            isThreadBusy: false);

        Assert.AreEqual(WorkThreadSkillActivationDecisionKind.RejectNoThread, decision.Kind);
        StringAssert.Contains(decision.Message, "Open");
    }

    [TestMethod]
    public void Plan_RejectsNativeSkillProvider()
    {
        var decision = new WorkThreadSkillActivationPlanner().Plan(
            CreateThread(AgentBackendIds.Codex.Value),
            isModelProviderReady: true,
            isThreadBusy: false);

        Assert.AreEqual(WorkThreadSkillActivationDecisionKind.RejectNativeSkillProvider, decision.Kind);
        StringAssert.Contains(decision.Message, "native skills");
    }

    [TestMethod]
    public void Plan_RejectsProviderNotReady()
    {
        var decision = new WorkThreadSkillActivationPlanner().Plan(
            CreateThread("local"),
            isModelProviderReady: false,
            isThreadBusy: false);

        Assert.AreEqual(WorkThreadSkillActivationDecisionKind.RejectProviderNotReady, decision.Kind);
    }

    [TestMethod]
    public void Plan_RejectsBusyThread()
    {
        var decision = new WorkThreadSkillActivationPlanner().Plan(
            CreateThread("local") with { Title = "Thread" },
            isModelProviderReady: true,
            isThreadBusy: true);

        Assert.AreEqual(WorkThreadSkillActivationDecisionKind.RejectThreadBusy, decision.Kind);
        StringAssert.Contains(decision.Message, "Thread");
    }

    private static WorkThreadDescriptorSnapshot CreateThread(string backendId)
        => new()
        {
            ThreadId = "thread-1",
            BackendId = backendId,
            BackendSessionId = "session-1",
            WorkingDirectory = "C:/project",
            Title = "Thread",
        };
}
