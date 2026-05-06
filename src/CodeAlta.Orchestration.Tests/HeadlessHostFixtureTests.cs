using System.Collections.Concurrent;
using System.Threading.Channels;
using CodeAlta.Agent;
using CodeAlta.Catalog.Skills;
using CodeAlta.Orchestration.Hosting;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class HeadlessHostFixtureTests
{
    [TestMethod]
    public async Task HeadlessHost_CreatesThreadSubmitsPromptStreamsEventsAndShutsDown()
    {
        using var temp = TempDirectory.Create();
        var backendId = new AgentBackendId("fake-headless");
        await using var host = await CodeAltaHost.CreateAsync(
            new CodeAltaHostOptions
            {
                GlobalRoot = temp.GlobalRoot,
                CurrentProjectPath = temp.ProjectRoot,
                IsHeadless = true,
                HasInteractiveUi = false,
                StartPlugins = false,
                ConfigureAgentBackends = factory => factory.Register(backendId, () => new FakeAgentBackend(backendId)),
            },
            CancellationToken.None);
        var executionOptions = new WorkThreadExecutionOptions
        {
            BackendId = backendId,
            ProviderKey = backendId.Value,
            WorkingDirectory = temp.ProjectRoot,
            ProjectRoots = [temp.ProjectRoot],
            OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.Deny)),
        };
        var streamedEvents = new List<WorkThreadRuntimeEvent>();
        using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receivedAssistantContent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streamTask = Task.Run(async () =>
        {
            await foreach (var runtimeEvent in host.RuntimeService.StreamEventsAsync(streamCts.Token))
            {
                streamedEvents.Add(runtimeEvent);
                if (runtimeEvent is WorkThreadAgentEvent { Event: AgentContentCompletedEvent completed } &&
                    string.Equals(completed.Content, "fake response", StringComparison.Ordinal))
                {
                    receivedAssistantContent.TrySetResult();
                    break;
                }
            }
        });

        var thread = await host.RuntimeService.CreateProjectThreadAsync(
            host.CurrentProject,
            executionOptions,
            title: "Headless sample",
            CancellationToken.None);

        var runId = await host.RuntimeService.SendAsync(
            thread,
            executionOptions,
            new AgentSendOptions { Input = AgentInput.Text("hello from headless") },
            CancellationToken.None);

        await receivedAssistantContent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await streamCts.CancelAsync();
        await streamTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        Assert.AreEqual("run-1", runId.Value);
        Assert.IsTrue(streamedEvents.OfType<WorkThreadLifecycleRuntimeEvent>().Any(static runtimeEvent =>
            runtimeEvent.Event.Kind == WorkThreadLifecycleEventKind.SessionStarted));
        Assert.IsTrue(streamedEvents.OfType<WorkThreadAgentEvent>().Any(static runtimeEvent =>
            runtimeEvent.Event is AgentSessionUpdateEvent { Kind: AgentSessionUpdateKind.Started }));
        Assert.IsTrue(streamedEvents.OfType<WorkThreadAgentEvent>().Any(static runtimeEvent =>
            runtimeEvent.Event is AgentContentCompletedEvent { Content: "fake response" }));
    }

    [TestMethod]
    public async Task HeadlessHost_ComposesPluginBackendsAndSkillRoots()
    {
        using var temp = TempDirectory.Create();
        var pluginBackendId = new AgentBackendId("shared-plugin");
        var skillRoot = Path.Combine(temp.Root, "plugin-skills");
        var skillDirectory = Path.Combine(skillRoot, "shared-host-skill");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(skillDirectory, "SKILL.md"),
            """
            ---
            name: shared-host-skill
            description: Skill contributed by the shared host plugin fixture.
            ---

            # Shared host skill

            Use this fixture skill from plugin resource roots.
            """);
        SharedHostFixturePlugin.SkillRootPath = skillRoot;
        SharedHostFixturePlugin.BackendId = pluginBackendId.Value;

        await using var host = await CodeAltaHost.CreateAsync(
            new CodeAltaHostOptions
            {
                GlobalRoot = temp.GlobalRoot,
                CurrentProjectPath = temp.ProjectRoot,
                IsHeadless = true,
                HasInteractiveUi = false,
                PluginBuiltIns =
                [
                    new BuiltInPluginDefinition
                    {
                        Id = "shared-host-fixture",
                        DisplayName = "Shared host fixture",
                        Factory = static () => new SharedHostFixturePlugin(),
                    },
                ],
            },
            CancellationToken.None);

        var models = await host.AgentHub.ListModelsAsync(pluginBackendId, CancellationToken.None);
        var skills = await host.SkillCatalog.ListAsync(
            new SkillCatalogQuery
            {
                Discovery = new SkillDiscoveryContext
                {
                    ProjectRoots = [temp.ProjectRoot],
                    UserCodeAltaRoot = temp.GlobalRoot,
                },
                IncludeUntrusted = true,
            },
            CancellationToken.None);

        Assert.AreEqual("Fake Model", models.Single().DisplayName);
        Assert.IsTrue(skills.Any(static skill =>
            string.Equals(skill.Name, "shared-host-skill", StringComparison.Ordinal) &&
            skill.SourceKind == SkillSourceKind.Plugin));
    }

    [TestMethod]
    public async Task HeadlessHost_DoesNotLeaveRunActiveWhenIdleArrivesBeforeSendReturns()
    {
        using var temp = TempDirectory.Create();
        var backendId = new AgentBackendId("fake-race");
        await using var host = await CodeAltaHost.CreateAsync(
            new CodeAltaHostOptions
            {
                GlobalRoot = temp.GlobalRoot,
                CurrentProjectPath = temp.ProjectRoot,
                IsHeadless = true,
                HasInteractiveUi = false,
                StartPlugins = false,
                ConfigureAgentBackends = factory => factory.Register(backendId, () => new FakeAgentBackend(backendId)),
            },
            CancellationToken.None);
        var executionOptions = new WorkThreadExecutionOptions
        {
            BackendId = backendId,
            ProviderKey = backendId.Value,
            WorkingDirectory = temp.ProjectRoot,
            ProjectRoots = [temp.ProjectRoot],
            OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.Deny)),
        };
        var thread = await host.RuntimeService.CreateProjectThreadAsync(
            host.CurrentProject,
            executionOptions,
            title: "Race sample",
            CancellationToken.None);

        var runId = await host.RuntimeService.SendAsync(
            thread,
            executionOptions,
            new AgentSendOptions { Input = AgentInput.Text("complete before returning") },
            CancellationToken.None);
        var hasActiveRun = await host.RuntimeService.HasActiveRunAsync(thread, CancellationToken.None);

        Assert.AreEqual("run-1", runId.Value);
        Assert.IsFalse(hasActiveRun);
    }

    private sealed class FakeAgentBackend(AgentBackendId backendId) : IAgentBackend
    {
        private int _nextSessionId;

        public AgentBackendId BackendId { get; } = backendId;

        public string DisplayName => "Fake Headless";

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentModelInfo>>([new AgentModelInfo("fake-model", DisplayName: "Fake Model")]);

        public Task<IReadOnlyList<AgentSessionMetadata>> ListSessionsAsync(
            AgentSessionListFilter? filter = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentSessionMetadata>>([]);

        public Task<IAgentSession> CreateSessionAsync(
            AgentSessionCreateOptions options,
            CancellationToken cancellationToken = default)
        {
            var sessionId = $"session-{Interlocked.Increment(ref _nextSessionId)}";
            return Task.FromResult<IAgentSession>(new FakeAgentSession(BackendId, sessionId, options.WorkingDirectory));
        }

        public Task<IAgentSession> ResumeSessionAsync(
            string sessionId,
            AgentSessionResumeOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            return Task.FromResult<IAgentSession>(new FakeAgentSession(BackendId, sessionId, options.WorkingDirectory));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class SharedHostFixturePlugin : PluginBase
    {
        public static string SkillRootPath { get; set; } = string.Empty;

        public static string BackendId { get; set; } = "shared-plugin";

        public override IEnumerable<PluginAgentBackendContribution> GetAgentBackends()
        {
            yield return PluginBackend.FromFactory(
                BackendId,
                static (context, _) => ValueTask.FromResult<IAgentBackend>(new FakeAgentBackend(new AgentBackendId(BackendId))),
                displayName: "Shared Plugin");
        }

        public override IEnumerable<PluginResourceContribution> GetResources()
        {
            yield return new PluginResourceContribution
            {
                Kind = PluginResourceKind.SkillRoot,
                Path = SkillRootPath,
                IsPackageRelative = false,
            };
        }
    }

    private sealed class FakeAgentSession(AgentBackendId backendId, string sessionId, string? workspacePath) : IAgentSession
    {
        private readonly ConcurrentDictionary<Guid, Action<AgentEvent>> _subscribers = new();
        private readonly Channel<AgentEvent> _events = Channel.CreateUnbounded<AgentEvent>();

        public AgentBackendId BackendId { get; } = backendId;

        public string SessionId { get; } = sessionId;

        public string? WorkspacePath { get; } = workspacePath;

        public IAsyncEnumerable<AgentEvent> StreamEventsAsync(CancellationToken cancellationToken = default)
            => _events.Reader.ReadAllAsync(cancellationToken);

        public IDisposable Subscribe(Action<AgentEvent> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            var id = Guid.NewGuid();
            _subscribers[id] = handler;
            return new Subscription(() => _subscribers.TryRemove(id, out _));
        }

        public Task<AgentRunId> SendAsync(AgentSendOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            var runId = new AgentRunId("run-1");
            Publish(new AgentSessionUpdateEvent(
                BackendId,
                SessionId,
                DateTimeOffset.UtcNow,
                runId,
                AgentSessionUpdateKind.Started,
                "Fake session started."));
            Publish(new AgentContentCompletedEvent(
                BackendId,
                SessionId,
                DateTimeOffset.UtcNow,
                runId,
                AgentContentKind.Assistant,
                "content-1",
                ParentActivityId: null,
                "fake response"));
            Publish(new AgentSessionUpdateEvent(
                BackendId,
                SessionId,
                DateTimeOffset.UtcNow,
                runId,
                AgentSessionUpdateKind.Idle,
                "Fake session idle."));
            return Task.FromResult(runId);
        }

        public Task<AgentRunId> SteerAsync(AgentSteerOptions options, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AbortAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CompactAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentEvent>>([]);

        public ValueTask DisposeAsync()
        {
            _events.Writer.TryComplete();
            _subscribers.Clear();
            return ValueTask.CompletedTask;
        }

        private void Publish(AgentEvent agentEvent)
        {
            _events.Writer.TryWrite(agentEvent);
            foreach (var subscriber in _subscribers.Values)
            {
                subscriber(agentEvent);
            }
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string root)
        {
            Root = root;
            GlobalRoot = Path.Combine(root, "global");
            ProjectRoot = Path.Combine(root, "project");
            Directory.CreateDirectory(GlobalRoot);
            Directory.CreateDirectory(ProjectRoot);
        }

        public string Root { get; }

        public string GlobalRoot { get; }

        public string ProjectRoot { get; }

        public static TempDirectory Create()
            => new(Path.Combine(Path.GetTempPath(), $"CodeAlta.HeadlessHost.{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
