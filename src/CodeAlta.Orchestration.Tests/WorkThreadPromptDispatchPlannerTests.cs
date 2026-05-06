using CodeAlta.Orchestration.Runtime.Prompts;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class WorkThreadPromptDispatchPlannerTests
{
    [TestMethod]
    public void Plan_SendsWhenSteerIsNotRequested()
    {
        var decision = new WorkThreadPromptDispatchPlanner().Plan(
            requestSteer: false,
            hasActiveRun: true,
            supportsSteering: true,
            queueIfSteeringUnsupported: true);

        Assert.AreEqual(WorkThreadPromptDispatchDecisionKind.Send, decision.Kind);
    }

    [TestMethod]
    public void Plan_SteersWhenRequestedAndActiveRunSupportsSteering()
    {
        var decision = new WorkThreadPromptDispatchPlanner().Plan(
            requestSteer: true,
            hasActiveRun: true,
            supportsSteering: true,
            queueIfSteeringUnsupported: false);

        Assert.AreEqual(WorkThreadPromptDispatchDecisionKind.Steer, decision.Kind);
    }

    [TestMethod]
    public void Plan_QueuesWhenSteeringUnsupportedAndQueueFallbackEnabled()
    {
        var decision = new WorkThreadPromptDispatchPlanner().Plan(
            requestSteer: true,
            hasActiveRun: true,
            supportsSteering: false,
            queueIfSteeringUnsupported: true);

        Assert.AreEqual(WorkThreadPromptDispatchDecisionKind.Queue, decision.Kind);
        StringAssert.Contains(decision.Message, "queued");
    }

    [TestMethod]
    public void Plan_RejectsWhenSteeringUnsupportedAndQueueFallbackDisabled()
    {
        var decision = new WorkThreadPromptDispatchPlanner().Plan(
            requestSteer: true,
            hasActiveRun: true,
            supportsSteering: false,
            queueIfSteeringUnsupported: false);

        Assert.AreEqual(WorkThreadPromptDispatchDecisionKind.Reject, decision.Kind);
        StringAssert.Contains(decision.Message, "not supported");
    }
}
