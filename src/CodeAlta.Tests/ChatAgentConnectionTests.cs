using CodeAlta.Agent;
using CodeAlta.Orchestration;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.Tests;

[TestClass]
public sealed class AgentSessionConnectionTests
{
    [TestMethod]
    public async Task EnsureConnectedAsync_RetriesAfterFailedStart_AndForwardsResponses()
    {
        using var temp = TempDirectory.Create();

        var backendFactory = new AgentBackendFactory();
        var backend = new FlakyBackend();
        backendFactory.Register("fakechat", () => backend);

        await using var hub = new AgentHub(backendFactory);
        var received = new List<AgentEvent>();
        await using var connection = new AgentSessionConnection(hub, received.Add);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => connection.EnsureConnectedAsync(
                    new AgentBackendId("fakechat"),
                    Environment.CurrentDirectory,
                    model: null,
                    reasoningEffort: null,
                    tools: null,
                    permissionRequestHandler: static (_, _) =>
                        Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                    userInputRequestHandler: null))
            .ConfigureAwait(false);

        Assert.IsNull(connection.CurrentSessionHandleId);
        Assert.IsFalse(connection.IsConnected);

        var sessionHandleId = await connection.EnsureConnectedAsync(
                new AgentBackendId("fakechat"),
                Environment.CurrentDirectory,
                model: null,
                reasoningEffort: null,
                tools: null,
                permissionRequestHandler: static (_, _) =>
                    Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                userInputRequestHandler: null)
            .ConfigureAwait(false);

        _ = await hub.RunAsync(
                sessionHandleId,
                new AgentSendOptions { Input = AgentInput.Text("hello") })
            .ConfigureAwait(false);

        Assert.AreEqual(2, backend.CreateSessionAttempts);
        Assert.AreEqual(sessionHandleId, connection.CurrentSessionHandleId!.Value);
        Assert.IsTrue(connection.IsConnected);
        Assert.IsTrue(received.OfType<AgentContentCompletedEvent>().Any(x => x.Kind == AgentContentKind.Assistant && x.Content == "ok"));
        Assert.IsTrue(received.OfType<AgentSessionUpdateEvent>().Any(x => x.Kind == AgentSessionUpdateKind.Idle));
    }

    [TestMethod]
    public async Task EnsureConnectedAsync_RecreatesSessionWhenModelOrReasoningChanges()
    {
        using var temp = TempDirectory.Create();

        var backendFactory = new AgentBackendFactory();
        var backend = new FlakyBackend(skipFirstFailure: true);
        backendFactory.Register("fakechat", () => backend);

        await using var hub = new AgentHub(backendFactory);
        await using var connection = new AgentSessionConnection(hub, static _ => { });

        var firstHandleId = await connection.EnsureConnectedAsync(
                new AgentBackendId("fakechat"),
                Environment.CurrentDirectory,
                model: "model-a",
                reasoningEffort: AgentReasoningEffort.Low,
                tools: null,
                permissionRequestHandler: static (_, _) =>
                    Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                userInputRequestHandler: null)
            .ConfigureAwait(false);

        var secondHandleId = await connection.EnsureConnectedAsync(
                new AgentBackendId("fakechat"),
                Environment.CurrentDirectory,
                model: "model-a",
                reasoningEffort: AgentReasoningEffort.Low,
                tools: null,
                permissionRequestHandler: static (_, _) =>
                    Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                userInputRequestHandler: null)
            .ConfigureAwait(false);

        var thirdHandleId = await connection.EnsureConnectedAsync(
                new AgentBackendId("fakechat"),
                Environment.CurrentDirectory,
                model: "model-b",
                reasoningEffort: AgentReasoningEffort.High,
                tools: null,
                permissionRequestHandler: static (_, _) =>
                    Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                userInputRequestHandler: null)
            .ConfigureAwait(false);

        Assert.AreEqual(firstHandleId, secondHandleId);
        Assert.AreNotEqual(firstHandleId, thirdHandleId);
        Assert.AreEqual(2, backend.CreateSessionAttempts);
        CollectionAssert.AreEqual(new[] { "model-a", "model-b" }, backend.CreatedModels);
        CollectionAssert.AreEqual(
            new[] { AgentReasoningEffort.Low, AgentReasoningEffort.High },
            backend.CreatedReasoningEfforts.ToArray());
        Assert.AreEqual(1, backend.DisposedSessionCount);
    }
    private sealed class FlakyBackend : IAgentBackend
    {
        private int _runCounter;
        private readonly bool _skipFirstFailure;

        public FlakyBackend(bool skipFirstFailure = false)
        {
            _skipFirstFailure = skipFirstFailure;
        }

        public AgentBackendId BackendId => new("fakechat");

        public string DisplayName => "Fake Chat";

        public int CreateSessionAttempts { get; private set; }

        public List<string?> CreatedModels { get; } = [];

        public List<AgentReasoningEffort> CreatedReasoningEfforts { get; } = [];

        public int DisposedSessionCount { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentModelInfo>>([]);

        public async IAsyncEnumerable<AgentSessionMetadata> ListSessionsAsync(
            AgentSessionListFilter? filter = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public Task<IAgentSession> CreateSessionAsync(
            AgentSessionCreateOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            CreateSessionAttempts++;
            CreatedModels.Add(options.Model);
            if (options.ReasoningEffort is { } reasoningEffort)
            {
                CreatedReasoningEfforts.Add(reasoningEffort);
            }

            if (!_skipFirstFailure && CreateSessionAttempts == 1)
            {
                throw new InvalidOperationException("simulated session startup failure");
            }

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
            private readonly FlakyBackend _backend;
            private readonly List<Action<AgentEvent>> _subscribers = [];
            private readonly object _subscriberLock = new();

            public FakeSession(FlakyBackend backend, string? sessionId = null)
            {
                _backend = backend;
                SessionId = sessionId ?? "fakechat-session";
            }

            public AgentBackendId BackendId => _backend.BackendId;

            public string SessionId { get; }

            public string? WorkspacePath => null;

            public ValueTask DisposeAsync()
            {
                _backend.DisposedSessionCount++;
                return ValueTask.CompletedTask;
            }

            public async IAsyncEnumerable<AgentEvent> StreamEventsAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
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
                Publish(new AgentContentCompletedEvent(
                    BackendId,
                    SessionId,
                    DateTimeOffset.UtcNow,
                    runId,
                    AgentContentKind.Assistant,
                    "assistant-1",
                    runId.Value,
                    "ok"));
                Publish(new AgentSessionUpdateEvent(
                    BackendId,
                    SessionId,
                    DateTimeOffset.UtcNow,
                    null,
                    AgentSessionUpdateKind.Idle,
                    null));
                return Task.FromResult(runId);
            }

            public Task<AgentRunId> SteerAsync(AgentSteerOptions options, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(options);
                throw new NotSupportedException();
            }

            public Task AbortAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task CompactAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<AgentEvent>>([]);

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
            ArgumentNullException.ThrowIfNull(dispose);
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
                $"CodeAlta.Tests.{Guid.NewGuid():N}");
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
            }
        }
    }
}
