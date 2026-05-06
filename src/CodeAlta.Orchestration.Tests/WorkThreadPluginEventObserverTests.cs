using CodeAlta.Agent;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Orchestration.Runtime.Plugins;
using CodeAlta.Plugins;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class WorkThreadPluginEventObserverTests
{
    [TestMethod]
    public async Task ObserveAsync_DispatchesHeadlessAgentEventWithoutActivePlugins()
    {
        var observer = new WorkThreadPluginEventObserver(new PluginOrchestrationBridge(
            new PluginContributionAdapterService(new PluginContributionRegistry()),
            static () => []));
        var context = new WorkThreadCommandContext
        {
            ProjectId = "project-1",
            ProjectPath = "C:/project",
            PromptSessionId = "prompt-session-1",
            ModelProviderId = "provider-1",
            ModelId = "model-1",
            ThreadId = "thread-1",
        };
        var agentEvent = new AgentErrorEvent(
            new AgentBackendId("provider-1"),
            "session-1",
            DateTimeOffset.UtcNow,
            "diagnostic");

        var diagnostics = await observer.ObserveAsync(context, agentEvent);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public void ObserveAsync_RejectsMissingContext()
    {
        var observer = new WorkThreadPluginEventObserver(new PluginOrchestrationBridge(
            new PluginContributionAdapterService(new PluginContributionRegistry()),
            static () => []));
        var agentEvent = new AgentErrorEvent(
            new AgentBackendId("provider-1"),
            "session-1",
            DateTimeOffset.UtcNow,
            "diagnostic");

        Assert.ThrowsExactly<ArgumentNullException>(() => observer.ObserveAsync(null!, agentEvent).AsTask().GetAwaiter().GetResult());
    }
}
