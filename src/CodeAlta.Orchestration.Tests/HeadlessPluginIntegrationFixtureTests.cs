using System.Text.Json;
using CodeAlta.Agent;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Orchestration.Runtime.Plugins;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class HeadlessPluginIntegrationFixtureTests
{
    [TestMethod]
    public async Task HeadlessPlugin_UsesRuntimeHooksToolsEventsAndThreadOrchestration()
    {
        HeadlessFixturePlugin.Reset();
        var orchestrator = new RecordingWorkThreadOrchestrator();
        HeadlessFixturePlugin.Threads = new PluginThreadOrchestrationService(orchestrator);
        var registry = new PluginContributionRegistry();
        var activator = new PluginRuntimeActivator(registry);
        var activation = await activator.ActivateAsync(
            CreateDiscovered<HeadlessFixturePlugin>(),
            sourcePackage: null,
            loadContext: null,
            new PluginActivationOptions { HostInfo = CreateHeadlessHostInfo() });
        Assert.IsTrue(activation.Succeeded, string.Join(Environment.NewLine, activation.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.IsNotNull(activation.ActivePlugin);
        var active = activation.ActivePlugin;
        var bridge = new PluginOrchestrationBridge(new PluginContributionAdapterService(registry), () => [active]);
        var options = new PluginAdapterOperationOptions
        {
            ProjectId = "project-1",
            ProjectPath = Environment.CurrentDirectory,
            ThreadId = "thread-1",
            BackendId = "provider-1",
            Model = "model-1",
        };

        var prompt = await bridge.ProcessPromptSubmittingAsync("hello", options: options);
        var before = await bridge.BeforeAgentRunAsync(CreateBeforeRunTemplate(active), options);
        var tools = bridge.GetAgentTools(options);
        var toolResult = await tools.Single().Definition.Handler(
            new AgentToolInvocation(new AgentBackendId("provider-1"), "session-1", "call-1", "headless_fixture", JsonSerializer.SerializeToElement(new { value = 1 })),
            CancellationToken.None);
        var eventDiagnostics = await bridge.ObserveAgentEventAsync(CreateAgentEventTemplate(active), options);

        Assert.AreEqual(0, prompt.Diagnostics.Count, string.Join(Environment.NewLine, prompt.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.AreEqual("hello processed callback", prompt.Result.ReplacementText);
        Assert.AreEqual(0, before.Diagnostics.Count, string.Join(Environment.NewLine, before.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.AreEqual("before-run", before.Result.AdditionalMessages.Single().Content);
        Assert.AreEqual("headless_fixture", before.Result.PreferredToolNames.Single());
        Assert.AreEqual("tool-result", ((AgentToolResultItem.Text)toolResult.Items.Single()).Value);
        Assert.AreEqual(0, eventDiagnostics.Count, string.Join(Environment.NewLine, eventDiagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.AreEqual(1, HeadlessFixturePlugin.AgentEventsObserved);
        Assert.AreEqual("before-run prompt", orchestrator.SubmitRequest?.Prompt);
        Assert.AreEqual("thread-1", orchestrator.SubmitRequest?.Context.ThreadId);
        Assert.IsTrue(active.RuntimeContext.Host.IsHeadless);
        Assert.IsFalse(active.RuntimeContext.Host.HasInteractiveUi);

        await active.DeactivateAsync(TimeSpan.FromSeconds(5));
    }

    private static DiscoveredPluginType CreateDiscovered<TPlugin>()
        where TPlugin : PluginBase
        => new()
        {
            Type = typeof(TPlugin),
            Descriptor = PluginDescriptorFactory.FromType(typeof(TPlugin)),
        };

    private static PluginHostInfo CreateHeadlessHostInfo()
        => new()
        {
            ApplicationName = "CodeAlta.Orchestration.Tests",
            Version = "1.0.0",
            HostApiVersion = "1.0.0",
            UserDataDirectory = Path.GetTempPath(),
            IsHeadless = true,
            HasInteractiveUi = false,
        };

    private static PluginBeforeAgentRunContext CreateBeforeRunTemplate(ActivePluginInstance active)
        => new()
        {
            Plugin = active.Descriptor,
            Services = active.RuntimeContext.Services,
            PromptText = "before-run prompt",
            ActiveToolNames = ["existing"],
        };

    private static PluginAgentEventContext CreateAgentEventTemplate(ActivePluginInstance active)
        => new()
        {
            Plugin = active.Descriptor,
            Services = active.RuntimeContext.Services,
            Event = new AgentActivityEvent(
                new AgentBackendId("provider-1"),
                "session-1",
                DateTimeOffset.UtcNow,
                RunId: null,
                AgentActivityKind.Turn,
                AgentActivityPhase.Started,
                ActivityId: "activity-1",
                ParentActivityId: null,
                Name: "turn",
                Message: "started"),
        };

    public sealed class HeadlessFixturePlugin : PluginBase
    {
        public static IPluginThreadOrchestrationService? Threads { get; set; }

        public static int AgentEventsObserved { get; private set; }

        public static void Reset()
        {
            Threads = null;
            AgentEventsObserved = 0;
        }

        public override IEnumerable<PluginPromptProcessorContribution> GetPromptProcessors()
        {
            yield return new PluginPromptProcessorContribution
            {
                Handler = static (context, _) => ValueTask.FromResult(PluginPromptResult.Replace(context.Text + " processed")),
            };
        }

        public override IEnumerable<PluginAgentToolContribution> GetAgentTools()
        {
            yield return new PluginAgentToolContribution
            {
                Definition = new AgentToolDefinition(
                    new AgentToolSpec("headless_fixture", "Headless fixture tool", JsonSerializer.SerializeToElement(new Dictionary<string, object?>())),
                    static (_, _) => Task.FromResult(new AgentToolResult(true, [new AgentToolResultItem.Text("tool-result")]))),
            };
        }

        public override ValueTask<PluginPromptResult?> OnPromptSubmittingAsync(PluginPromptSubmittingContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<PluginPromptResult?>(PluginPromptResult.Replace(context.Text + " callback"));

        public override async ValueTask<PluginBeforeAgentRunResult?> OnBeforeAgentRunAsync(PluginBeforeAgentRunContext context, CancellationToken cancellationToken = default)
        {
            if (Threads is null)
            {
                throw new InvalidOperationException("Thread orchestration service was not configured for the fixture plugin.");
            }

            await Threads.SubmitPromptAsync(
                new SubmitWorkThreadPromptRequest
                {
                    Context = new WorkThreadCommandContext
                    {
                        ProjectId = context.ProjectId ?? "project-1",
                        ProjectPath = context.ProjectPath ?? Environment.CurrentDirectory,
                        ThreadId = context.ThreadId ?? "thread-1",
                        PromptSessionId = "prompt-session-1",
                        ModelProviderId = context.BackendId ?? "provider-1",
                    },
                    Prompt = context.PromptText ?? string.Empty,
                },
                cancellationToken);

            return new PluginBeforeAgentRunResult
            {
                AdditionalMessages = [new PluginPromptMessage { Role = PluginPromptMessageRole.Developer, Content = "before-run" }],
                PreferredToolNames = ["headless_fixture"],
            };
        }

        public override ValueTask OnAgentEventAsync(PluginAgentEventContext context, CancellationToken cancellationToken = default)
        {
            AgentEventsObserved++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingWorkThreadOrchestrator : IWorkThreadOrchestrator
    {
        public SubmitWorkThreadPromptRequest? SubmitRequest { get; private set; }

        public ValueTask<WorkThreadCommandResult> CreateDraftAsync(CreateWorkThreadDraftRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

        public ValueTask<WorkThreadCommandResult> LaunchThreadAsync(LaunchWorkThreadRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

        public ValueTask<WorkThreadCommandResult> SubmitPromptAsync(SubmitWorkThreadPromptRequest request, CancellationToken cancellationToken = default)
        {
            SubmitRequest = request;
            return new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Submitted });
        }

        public ValueTask<WorkThreadCommandResult> SteerAsync(SteerWorkThreadRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Steered });

        public ValueTask<WorkThreadCommandResult> AbortAsync(AbortWorkThreadRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

        public ValueTask<WorkThreadCommandResult> CompactAsync(CompactWorkThreadRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

        public ValueTask<WorkThreadCommandResult> ActivateSkillAsync(ActivateSkillRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

        public ValueTask<WorkThreadCommandResult> QueuePromptAsync(QueueWorkThreadPromptRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Queued });

        public ValueTask<WorkThreadSnapshot?> GetThreadSnapshotAsync(string threadId, CancellationToken cancellationToken = default)
            => new((WorkThreadSnapshot?)null);

        public async IAsyncEnumerable<WorkThreadOrchestratorEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
