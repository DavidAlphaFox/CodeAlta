using System.Text.Json;
using CodeAlta.Agent;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Orchestration.Runtime.Plugins;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class SessionPluginToolServiceTests
{
    [TestMethod]
    public void MergeTools_AppendsPluginToolsAndPreferredNames()
    {
        var existing = CreateTool("existing");
        var pluginTool = CreateTool("plugin_tool");
        var service = CreateService(pluginTool);
        var options = CreateOptions([existing], ["existing"]);

        var merged = service.MergeTools(options);

        Assert.AreNotSame(options, merged);
        Assert.AreEqual(2, merged.Tools?.Count);
        Assert.AreSame(existing, merged.Tools![0]);
        Assert.AreSame(pluginTool, merged.Tools[1]);
        CollectionAssert.AreEqual(new[] { "existing", "plugin_tool" }, merged.PreferredToolNames.ToArray());
        Assert.AreEqual(options.ProviderId, merged.ProviderId);
        Assert.AreSame(options.OnPermissionRequest, merged.OnPermissionRequest);
    }

    [TestMethod]
    public void MergeTools_SkipsDuplicatePluginToolNames()
    {
        var existing = CreateTool("duplicate");
        var service = CreateService(CreateTool("duplicate"));
        var options = CreateOptions([existing], []);

        var merged = service.MergeTools(options);

        Assert.AreEqual(1, merged.Tools?.Count);
        Assert.AreSame(existing, merged.Tools![0]);
    }

    [TestMethod]
    public void MergeTools_ReturnsSourceOptionsWhenNoPluginToolsApply()
    {
        var bridge = new PluginOrchestrationBridge(
            new PluginContributionAdapterService(new PluginContributionRegistry()),
            static () => []);
        var service = new SessionPluginToolService(bridge);
        var options = CreateOptions([], []);

        var merged = service.MergeTools(options);

        Assert.AreSame(options, merged);
    }

    private static SessionPluginToolService CreateService(AgentToolDefinition tool)
    {
        var registry = new PluginContributionRegistry();
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
            [new PluginAgentToolContribution { Definition = tool }],
            activationGeneration: 1);
        return new SessionPluginToolService(new PluginOrchestrationBridge(
            new PluginContributionAdapterService(registry),
            static () => []));
    }

    private static AgentToolDefinition CreateTool(string name)
        => new(
            new AgentToolSpec(name, name, JsonDocument.Parse("{}").RootElement.Clone()),
            static (_, _) => Task.FromResult(new AgentToolResult(Success: true, [])));

    private static SessionExecutionOptions CreateOptions(
        IReadOnlyList<AgentToolDefinition> tools,
        IReadOnlyList<string> preferredToolNames)
        => new()
        {
            ProviderId = new ModelProviderId("local"),
            WorkingDirectory = "C:/project",
            Tools = tools,
            PreferredToolNames = preferredToolNames,
            OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
        };
}
