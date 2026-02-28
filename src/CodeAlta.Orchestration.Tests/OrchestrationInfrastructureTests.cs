using System.Text.Json;
using CodeAlta.Agent;
using CodeAlta.Orchestration;
using CodeAlta.Orchestration.Context;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Persistence;
using CodeAlta.Workspaces.Roles;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class OrchestrationInfrastructureTests
{
    [TestMethod]
    public async Task RoleProfileStore_ParsesFrontmatterAndCopilotMarkdown()
    {
        using var temp = TempDirectory.Create();
        var rolesRoot = Path.Combine(temp.Path, ".codealta", "roles");
        Directory.CreateDirectory(rolesRoot);

        var frontmatterRolePath = Path.Combine(rolesRoot, "planner.workspace.md");
        await File.WriteAllTextAsync(
            frontmatterRolePath,
            """
            ---
            id: planner.workspace
            name: Workspace Planner
            description: Plans workspace goals into durable tasks.
            tools_allowed:
              - codealta.tasks
              - codealta.artifacts
            default_backend: codex
            ---
            Create and maintain a clear task tree with acceptance criteria.
            """
        ).ConfigureAwait(false);

        var copilotRolePath = Path.Combine(rolesRoot, "reviewer.md");
        await File.WriteAllTextAsync(
            copilotRolePath,
            """
            # Reviewer

            Reviews diffs for regressions and risk.

            Focus on correctness, missing tests, and user-facing behavior.
            """
        ).ConfigureAwait(false);

        var store = new RoleProfileStore();
        var profiles = await store.LoadAsync([rolesRoot]).ConfigureAwait(false);

        Assert.AreEqual(2, profiles.Count);
        var planner = profiles.Single(x => x.RoleId == "planner.workspace");
        Assert.AreEqual("Workspace Planner", planner.Name);
        CollectionAssert.Contains(planner.ToolsPolicy.Allowed.ToArray(), "codealta.tasks");
        Assert.AreEqual("codex", planner.DefaultBackend);

        var reviewer = profiles.Single(x => x.RoleId == "reviewer");
        Assert.AreEqual("Reviewer", reviewer.Name);
        StringAssert.Contains(reviewer.Description, "Reviews");
    }

    [TestMethod]
    public async Task ContextPackBuilder_EnforcesBudgetAndPreservesSourceLinks()
    {
        var providerA = new StaticContextProvider(
        [
            new ContextItem
            {
                Title = "File A",
                Content = "01234567890123456789",
                SourceUri = "file://repo/src/A.cs#L10",
                Priority = 10,
            },
        ]);
        var providerB = new StaticContextProvider(
        [
            new ContextItem
            {
                Title = "File B",
                Content = "abcdefghijklmnopqrstuvwxyz",
                SourceUri = "file://repo/src/B.cs#L20",
                Priority = 20,
            },
        ]);

        var builder = new ContextPackBuilder([providerA, providerB]);
        var pack = await builder.BuildAsync(
            new AgentScope { Kind = AgentScopeKind.Project, Id = "project-1" },
            "implement parser",
            maxCharacters: 40).ConfigureAwait(false);

        Assert.IsTrue(pack.Truncated);
        Assert.IsTrue(pack.TotalCharacters <= 40);
        Assert.IsTrue(pack.Items.Any(x => x.SourceUri == "codealta://request"));
        Assert.IsTrue(pack.Items.Any(x => x.SourceUri.StartsWith("file://", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task OrchestrationFlow_PlannerCreatesAndBuilderCompletesTasks()
    {
        using var temp = TempDirectory.Create();
        var db = await CreateDbAsync(temp.Path).ConfigureAwait(false);
        var taskRepository = new TaskRepository(db);
        var artifactStore = new ArtifactStore();
        var artifactRepository = new ArtifactRepository(db);
        var options = new OrchestrationOptions { ArtifactRoot = Path.Combine(temp.Path, "orchestration") };

        var planner = new PlannerService(taskRepository, artifactStore, artifactRepository, options);
        var builder = new BuilderService(taskRepository, artifactStore, artifactRepository, options);

        var plan = await planner.CreatePlanAsync(
            new PlannerPlanRequest
            {
                Goal = "Implement orchestration flow",
                WorkspaceId = "workspace-1",
                ProjectId = "project-1",
                Steps = ["Add planner", "Add builder"],
            }).ConfigureAwait(false);

        Assert.AreEqual(2, plan.ChildTaskIds.Count);
        var root = await taskRepository.GetAsync(plan.RootTaskId).ConfigureAwait(false);
        Assert.IsNotNull(root);
        Assert.AreEqual(CodeAlta.Persistence.TaskStatus.Pending, root.Status);

        var planArtifact = await artifactRepository.GetByIdAsync(plan.PlanArtifactId).ConfigureAwait(false);
        Assert.IsNotNull(planArtifact);
        StringAssert.Contains(planArtifact.Type, "plan.output");

        var buildResult = await builder.CompleteTaskAsync(
            new BuilderExecutionRequest
            {
                TaskId = plan.ChildTaskIds[0],
                WorkspaceId = "workspace-1",
                ProjectId = "project-1",
                VerificationSummary = "dotnet build and dotnet test passed.",
            }).ConfigureAwait(false);

        var completed = await taskRepository.GetAsync(buildResult.TaskId).ConfigureAwait(false);
        Assert.IsNotNull(completed);
        Assert.AreEqual(CodeAlta.Persistence.TaskStatus.Completed, completed.Status);

        var verificationArtifact = await artifactRepository.GetByIdAsync(buildResult.VerificationArtifactId).ConfigureAwait(false);
        Assert.IsNotNull(verificationArtifact);
        Assert.AreEqual("builder.verification", verificationArtifact.Type);
    }

    [TestMethod]
    public async Task AgentHub_RunAsync_WithFakeBackend_EmitsRunEvents()
    {
        using var temp = TempDirectory.Create();
        var db = await CreateDbAsync(temp.Path).ConfigureAwait(false);
        var repository = new AgentRepository(db);

        var backendFactory = new AgentBackendFactory();
        backendFactory.Register("fake", static () => new FakeBackend());

        await using var hub = new AgentHub(backendFactory, repository);
        var identity = await hub.RegisterAgentAsync(
            "builder.project",
            new AgentScope { Kind = AgentScopeKind.Project, Id = "project-1" },
            new AgentBackendId("fake")).ConfigureAwait(false);

        await hub.StartSessionAsync(
            identity.AgentId,
            new AgentSessionCreateOptions
            {
                OnPermissionRequest = static (_, _) =>
                    Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
            }).ConfigureAwait(false);

        var runId = await hub.RunAsync(
            identity.AgentId,
            new AgentSendOptions { Input = AgentInput.Text("hello") }).ConfigureAwait(false);
        Assert.AreEqual("fake-run-1", runId.ToString());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var events = new List<OrchestrationEvent>();
        await foreach (var @event in hub.StreamEventsAsync(cts.Token))
        {
            events.Add(@event);
            if (events.Count >= 2)
            {
                break;
            }
        }

        Assert.IsTrue(events.OfType<RunStartedEvent>().Any());
        Assert.IsTrue(events.OfType<RunCompletedEvent>().Any());
    }

    [TestMethod]
    public async Task AgentHub_SubscribeSessionEventsAsync_ForwardsSessionEvents()
    {
        using var temp = TempDirectory.Create();
        var db = await CreateDbAsync(temp.Path).ConfigureAwait(false);
        var repository = new AgentRepository(db);

        var backendFactory = new AgentBackendFactory();
        backendFactory.Register("fake", static () => new FakeBackend());

        await using var hub = new AgentHub(backendFactory, repository);
        var identity = await hub.RegisterAgentAsync(
            "builder.project",
            new AgentScope { Kind = AgentScopeKind.Project, Id = "project-1" },
            new AgentBackendId("fake")).ConfigureAwait(false);

        await hub.StartSessionAsync(
            identity.AgentId,
            new AgentSessionCreateOptions
            {
                OnPermissionRequest = static (_, _) =>
                    Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
            }).ConfigureAwait(false);

        var received = new List<AgentEvent>();
        using var subscription = await hub.SubscribeSessionEventsAsync(
                identity.AgentId,
                received.Add)
            .ConfigureAwait(false);

        _ = await hub.RunAsync(
                identity.AgentId,
                new AgentSendOptions { Input = AgentInput.Text("hello") })
            .ConfigureAwait(false);

        Assert.IsTrue(received.OfType<AgentAssistantMessageEvent>().Any());
        Assert.IsTrue(received.OfType<AgentSessionIdleEvent>().Any());
    }

    private static async Task<CodeAltaDb> CreateDbAsync(string rootPath)
    {
        var dbPath = Path.Combine(rootPath, "state", "db", "codealta.db");
        var db = new CodeAltaDb(new CodeAltaDbOptions { DatabasePath = dbPath });
        await db.InitializeAsync().ConfigureAwait(false);
        return db;
    }

    private sealed class StaticContextProvider : IContextProvider
    {
        private readonly IReadOnlyList<ContextItem> _items;

        public StaticContextProvider(IReadOnlyList<ContextItem> items)
        {
            _items = items;
        }

        public Task<IReadOnlyList<ContextItem>> ProvideAsync(
            ContextProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Task.FromResult(_items);
        }
    }

    private sealed class FakeBackend : IAgentBackend
    {
        private int _runCounter;

        public AgentBackendId BackendId => new("fake");

        public string DisplayName => "Fake";

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AgentModelInfo>>([]);
        }

        public Task<IReadOnlyList<AgentSessionMetadata>> ListSessionsAsync(
            AgentSessionListFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AgentSessionMetadata>>([]);
        }

        public Task<IAgentSession> CreateSessionAsync(
            AgentSessionCreateOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            return Task.FromResult<IAgentSession>(new FakeSession(this));
        }

        public Task<IAgentSession> ResumeSessionAsync(
            string sessionId,
            AgentSessionResumeOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            return Task.FromResult<IAgentSession>(new FakeSession(this, sessionId));
        }

        private sealed class FakeSession : IAgentSession
        {
            private readonly FakeBackend _backend;
            private readonly List<AgentEvent> _events = [];
            private readonly List<Action<AgentEvent>> _subscribers = [];
            private readonly object _subscriberLock = new();

            public FakeSession(FakeBackend backend, string? sessionId = null)
            {
                _backend = backend;
                SessionId = sessionId ?? "fake-session-1";
            }

            public AgentBackendId BackendId => _backend.BackendId;

            public string SessionId { get; }

            public string? WorkspacePath => null;

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }

            public async IAsyncEnumerable<AgentEvent> StreamEventsAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (var @event in _events.ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return @event;
                    await Task.Yield();
                }
            }

            public IDisposable Subscribe(Action<AgentEvent> handler)
            {
                ArgumentNullException.ThrowIfNull(handler);

                lock (_subscriberLock)
                {
                    _subscribers.Add(handler);
                }

                return DisposableAction.Create(() =>
                {
                    lock (_subscriberLock)
                    {
                        _subscribers.Remove(handler);
                    }
                });
            }

            public Task<AgentRunId> SendAsync(AgentSendOptions options, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(options);
                var runId = new AgentRunId($"fake-run-{Interlocked.Increment(ref _backend._runCounter)}");
                var message = new AgentAssistantMessageEvent(
                    BackendId,
                    SessionId,
                    DateTimeOffset.UtcNow,
                    runId,
                    "ok");
                var idle = new AgentSessionIdleEvent(
                    BackendId,
                    SessionId,
                    DateTimeOffset.UtcNow);

                _events.Add(message);
                _events.Add(idle);

                Publish(message);
                Publish(idle);
                return Task.FromResult(runId);
            }

            private void Publish(AgentEvent @event)
            {
                Action<AgentEvent>[] subscribers;
                lock (_subscriberLock)
                {
                    subscribers = _subscribers.ToArray();
                }

                foreach (var subscriber in subscribers)
                {
                    subscriber(@event);
                }
            }

            public Task AbortAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<AgentEvent>>(_events.ToArray());
            }
        }
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;

        private DisposableAction(Action dispose)
        {
            _dispose = dispose;
        }

        public static IDisposable Create(Action dispose)
        {
            return new DisposableAction(dispose);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _dispose();
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CodeAlta.Orchestration.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for temporary test files.
            }
        }
    }
}
