using CodeAlta.Orchestration.Runtime.Prompts;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class SessionPromptDispatchPlannerTests
{
    [TestMethod]
    public void Plan_SendsWhenSteerIsNotRequested()
    {
        var decision = new SessionPromptDispatchPlanner().Plan(
            requestSteer: false,
            hasActiveRun: true,
            supportsSteering: true,
            queueIfSteeringUnsupported: true);

        Assert.AreEqual(SessionViewPromptDispatchDecisionKind.Send, decision.Kind);
    }

    [TestMethod]
    public void Plan_SteersWhenRequestedAndActiveRunSupportsSteering()
    {
        var decision = new SessionPromptDispatchPlanner().Plan(
            requestSteer: true,
            hasActiveRun: true,
            supportsSteering: true,
            queueIfSteeringUnsupported: false);

        Assert.AreEqual(SessionViewPromptDispatchDecisionKind.Steer, decision.Kind);
    }

    [TestMethod]
    public void Plan_QueuesWhenSteeringUnsupportedAndQueueFallbackEnabled()
    {
        var decision = new SessionPromptDispatchPlanner().Plan(
            requestSteer: true,
            hasActiveRun: true,
            supportsSteering: false,
            queueIfSteeringUnsupported: true);

        Assert.AreEqual(SessionViewPromptDispatchDecisionKind.Queue, decision.Kind);
        StringAssert.Contains(decision.Message, "queued");
    }

    [TestMethod]
    public void Plan_RejectsWhenSteeringUnsupportedAndQueueFallbackDisabled()
    {
        var decision = new SessionPromptDispatchPlanner().Plan(
            requestSteer: true,
            hasActiveRun: true,
            supportsSteering: false,
            queueIfSteeringUnsupported: false);

        Assert.AreEqual(SessionViewPromptDispatchDecisionKind.Reject, decision.Kind);
        StringAssert.Contains(decision.Message, "not supported");
    }
}
