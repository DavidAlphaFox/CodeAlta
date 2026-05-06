using System.Text.Json;
using CodeAlta.Agent;
using CodeAlta.Orchestration.Runtime.Plugins;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class PluginOrchestrationBridgeTests
{
    [TestMethod]
    public async Task ProcessPromptSubmittingAsync_UsesHeadlessDefaultsWithoutActivePlugins()
    {
        var bridge = CreateBridge(new PluginContributionRegistry());

        var result = await bridge.ProcessPromptSubmittingAsync("hello", cancellationToken: CancellationToken.None);

        Assert.AreEqual(PluginPromptDisposition.Replace, result.Result.Disposition);
        Assert.AreEqual("hello", result.Result.ReplacementText);
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    [TestMethod]
    public void GetAgentTools_ReturnsHeadlessApplicableToolContributions()
    {
        var registry = new PluginContributionRegistry();
        var tool = new PluginAgentToolContribution
        {
            Definition = new AgentToolDefinition(
                new AgentToolSpec("tool_1", "Tool", JsonDocument.Parse("{}").RootElement.Clone()),
                static (_, _) => Task.FromResult(new AgentToolResult(Success: true, []))),
        };
        registry.Register(
            new PluginDescriptor
            {
                RuntimeKey = "plugin-1",
                TypeName = "Plugin",
                AssemblyName = "PluginAssembly",
            },
            PluginScope.Global,
            scopeProjectId: null,
            scopeProjectPath: null,
            PluginPoint.AgentTool,
            [tool],
            activationGeneration: 1);
        var bridge = CreateBridge(registry);

        var tools = bridge.GetAgentTools(new PluginAdapterOperationOptions { HasInteractiveUi = true });

        Assert.AreEqual(1, tools.Count);
        Assert.AreSame(tool, tools[0]);
    }

    [TestMethod]
    public async Task RunCompactionAsync_ReturnsEmptyResultWhenNoCompactionContributionsApply()
    {
        var bridge = CreateBridge(new PluginContributionRegistry());

        var result = await bridge.RunCompactionAsync(cancellationToken: CancellationToken.None);

        Assert.AreEqual(0, result.BeforeResults.Count);
        Assert.AreEqual(0, result.InstructionResults.Count);
        Assert.AreEqual(0, result.ReducerResults.Count);
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    private static PluginOrchestrationBridge CreateBridge(PluginContributionRegistry registry)
        => new(new PluginContributionAdapterService(registry), static () => []);
}
