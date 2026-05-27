using CodeAlta.Orchestration.Runtime;
using CodeAlta.Orchestration.Runtime.Plugins;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class SessionCompactionPluginServiceTests
{
    [TestMethod]
    public async Task RunHooksAsync_UsesHeadlessBridgeForExplicitCompactionRequest()
    {
        var bridge = new PluginOrchestrationBridge(
            new PluginContributionAdapterService(new PluginContributionRegistry()),
            static () => []);
        var service = new SessionCompactionPluginService(bridge);
        var request = new CompactSessionRequest
        {
            Context = new SessionCommandContext
            {
                ProjectId = "project-1",
                ProjectPath = "C:/project",
                PromptSessionId = "prompt-session-1",
                ModelProviderId = "provider-1",
                ModelId = "model-1",
                SessionId = "session-1",
            },
        };

        var result = await service.RunHooksAsync(request);

        Assert.AreEqual(0, result.BeforeResults.Count);
        Assert.AreEqual(0, result.InstructionResults.Count);
        Assert.AreEqual(0, result.ReducerResults.Count);
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    [TestMethod]
    public void RunHooksAsync_RejectsMissingRequest()
    {
        var bridge = new PluginOrchestrationBridge(
            new PluginContributionAdapterService(new PluginContributionRegistry()),
            static () => []);
        var service = new SessionCompactionPluginService(bridge);

        Assert.ThrowsExactly<ArgumentNullException>(() => service.RunHooksAsync(null!).AsTask().GetAwaiter().GetResult());
    }
}
