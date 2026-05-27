using CodeAlta.Agent;
using CodeAlta.Orchestration;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.Tests;

[TestClass]
public sealed class AgentHubBackendReloadTests
{
    [TestMethod]
    public async Task StartSessionAsync_UsesProviderKeyAndReturnsTransientHandle()
    {
        var providerA = new TrackingBackend("provider-a");
        var providerB = new TrackingBackend("provider-b");
        var factory = new AgentBackendFactory();
        factory.Register("provider-a", () => providerA);
        factory.Register("provider-b", () => providerB);

        await using var hub = new AgentHub(factory);

        var handle = await hub.StartSessionAsync(CreateOptions("provider-b")).ConfigureAwait(false);

        Assert.AreNotEqual(default, handle.HandleId);
        Assert.AreEqual("provider-b-session-1", handle.SessionId);
        Assert.AreEqual(new AgentBackendId("provider-b"), handle.ProviderId);
        Assert.AreEqual(0, providerA.StartCount);
        Assert.AreEqual(1, providerB.StartCount);
        Assert.AreEqual("provider-b", providerB.LastCreateOptions?.ProviderKey);

        await hub.StopSessionAsync(handle.HandleId).ConfigureAwait(false);

        Assert.AreEqual(1, providerB.StopCount);
        Assert.AreEqual(1, providerB.DisposeCount);
    }

    [TestMethod]
    public async Task RunAsync_SerializesSameSessionButAllowsDifferentSessionsToRunConcurrently()
    {
        var backends = new List<TrackingBackend>();
        var factory = new AgentBackendFactory();
        factory.Register("provider", () =>
        {
            var backend = new TrackingBackend("provider") { CreateBlockingSessions = true };
            backends.Add(backend);
            return backend;
        });

        await using var hub = new AgentHub(factory);
        var first = await hub.StartSessionAsync(CreateOptions("provider")).ConfigureAwait(false);
        var second = await hub.StartSessionAsync(CreateOptions("provider")).ConfigureAwait(false);
        var firstSession = (BlockingSession)backends[0].LastSession!;
        var secondSession = (BlockingSession)backends[1].LastSession!;

        var firstRun = hub.RunAsync(first.HandleId, CreateSendOptions("first"));
        await firstSession.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        var sameSessionRun = hub.RunAsync(first.HandleId, CreateSendOptions("same-session"));
        await Task.Delay(100).ConfigureAwait(false);
        Assert.IsFalse(sameSessionRun.IsCompleted, "A second run for the same session should wait for the session run gate.");

        var otherSessionRunTask = hub.RunAsync(second.HandleId, CreateSendOptions("other-session"));
        await secondSession.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        secondSession.ReleaseFirstSend.TrySetResult();
        var otherSessionRun = await otherSessionRunTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.AreEqual(new AgentRunId(secondSession.FirstRunId), otherSessionRun);

        firstSession.ReleaseFirstSend.TrySetResult();
        Assert.AreEqual(new AgentRunId(firstSession.FirstRunId), await firstRun.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        Assert.AreEqual(new AgentRunId(firstSession.SecondRunId), await sameSessionRun.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        Assert.AreEqual(2, firstSession.SendCount);
    }

    [TestMethod]
    public async Task SteerAbortAndCompactUsePerSessionCoordination()
    {
        var backend = new TrackingBackend("provider") { CreateBlockingSessions = true };
        var factory = new AgentBackendFactory();
        factory.Register("provider", () => backend);

        await using var hub = new AgentHub(factory);
        var handle = await hub.StartSessionAsync(CreateOptions("provider")).ConfigureAwait(false);
        var session = (BlockingSession)backend.LastSession!;

        var run = hub.RunAsync(handle.HandleId, CreateSendOptions("run"));
        await session.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        var steerRunId = await hub.SteerAsync(
                handle.HandleId,
                new AgentSteerOptions
                {
                    Input = AgentInput.Text("steer"),
                    ExpectedRunId = new AgentRunId(session.FirstRunId),
                })
            .WaitAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(false);
        Assert.AreEqual(new AgentRunId(session.FirstRunId), steerRunId);

        await hub.AbortAsync(handle.HandleId).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.AreEqual(1, session.AbortCount);

        var compact = hub.CompactAsync(handle.HandleId);
        await Task.Delay(100).ConfigureAwait(false);
        Assert.IsFalse(compact.IsCompleted, "Compaction should wait for the same session's active run gate.");

        session.ReleaseFirstSend.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var outcome = await compact.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.AreEqual(1, session.CompactCount);
        Assert.IsTrue(outcome?.Success);
    }

    [TestMethod]
    public async Task ParentAndChildSessionsHaveIndependentRunGatesAndLineageEvents()
    {
        var backends = new List<TrackingBackend>();
        var factory = new AgentBackendFactory();
        factory.Register("provider", () =>
        {
            var backend = new TrackingBackend("provider") { CreateBlockingSessions = true };
            backends.Add(backend);
            return backend;
        });

        await using var hub = new AgentHub(factory);
        var parent = await hub.StartSessionAsync(CreateOptions("provider", threadId: "parent-session")).ConfigureAwait(false);
        var child = await hub.StartSessionAsync(CreateOptions("provider", threadId: "child-session", parentSessionId: parent.SessionId)).ConfigureAwait(false);
        var parentSession = (BlockingSession)backends[0].LastSession!;

        var parentRun = hub.RunAsync(parent.HandleId, CreateSendOptions("parent"));
        await parentSession.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        var childSession = (BlockingSession)backends[1].LastSession!;
        var childRunTask = hub.RunAsync(child.HandleId, CreateSendOptions("child"));
        await childSession.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        childSession.ReleaseFirstSend.TrySetResult();
        var childRunId = await childRunTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.AreEqual(new AgentRunId(childSession.FirstRunId), childRunId);
        Assert.IsFalse(parentRun.IsCompleted, "Child sessions must not share the parent session run gate.");

        parentSession.ReleaseFirstSend.TrySetResult();
        await parentRun.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        var events = await CollectEventsAsync(hub, 6).ConfigureAwait(false);
        Assert.IsTrue(events.OfType<AgentSessionAttachedEvent>().Any(e => e.SessionHandleId == parent.HandleId && e.ParentSessionId is null));
        Assert.IsTrue(events.OfType<AgentSessionAttachedEvent>().Any(e => e.SessionHandleId == child.HandleId && e.ParentSessionId == parent.SessionId));
        Assert.IsTrue(events.OfType<RunCompletedEvent>().Any(e => e.SessionHandleId == parent.HandleId));
        Assert.IsTrue(events.OfType<RunCompletedEvent>().Any(e => e.SessionHandleId == child.HandleId));
    }

    [TestMethod]
    public async Task StopSessionAsync_WaitsForActiveRunBeforeDisposingSession()
    {
        var backend = new TrackingBackend("provider") { CreateBlockingSessions = true };
        var factory = new AgentBackendFactory();
        factory.Register("provider", () => backend);

        await using var hub = new AgentHub(factory);
        var handle = await hub.StartSessionAsync(CreateOptions("provider")).ConfigureAwait(false);
        var session = (BlockingSession)backend.LastSession!;

        var runTask = hub.RunAsync(handle.HandleId, CreateSendOptions("run"));
        await session.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        var stopTask = hub.StopSessionAsync(handle.HandleId);
        Assert.IsFalse(stopTask.IsCompleted);

        session.ReleaseFirstSend.TrySetResult();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.AreEqual(1, session.DisposeCount);
        Assert.AreEqual(1, backend.DisposeCount);
    }

    private static AgentSessionCreateOptions CreateOptions(string providerKey, string? threadId = null, string? parentSessionId = null)
        => new()
        {
            ThreadId = threadId,
            ParentSessionId = parentSessionId,
            ProviderKey = providerKey,
            WorkingDirectory = Environment.CurrentDirectory,
            OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
        };

    private static AgentSendOptions CreateSendOptions(string text)
        => new() { Input = AgentInput.Text(text) };

    private static async Task<IReadOnlyList<OrchestrationEvent>> CollectEventsAsync(AgentHub hub, int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<OrchestrationEvent>();
        await foreach (var @event in hub.StreamEventsAsync(cts.Token).ConfigureAwait(false))
        {
            events.Add(@event);
            if (events.Count >= count)
            {
                break;
            }
        }

        return events;
    }

    private sealed class TrackingBackend(string providerId) : IAgentBackend
    {
        private int _sessionCounter;

        public AgentBackendId BackendId { get; } = new(providerId);

        public string DisplayName => BackendId.Value;

        public bool CreateBlockingSessions { get; init; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public AgentSessionCreateOptions? LastCreateOptions { get; private set; }

        public IAgentSession? LastSession { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentModelInfo>>([]);

        public async IAsyncEnumerable<AgentSessionMetadata> ListSessionsAsync(
            AgentSessionListFilter? filter = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public Task<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IAgentSession> CreateSessionAsync(
            AgentSessionCreateOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            LastCreateOptions = options;
            var sessionNumber = Interlocked.Increment(ref _sessionCounter);
            LastSession = CreateBlockingSessions
                ? new BlockingSession(BackendId, options.ThreadId ?? $"{BackendId.Value}-session-{sessionNumber}")
                : new ImmediateSession(BackendId, options.ThreadId ?? $"{BackendId.Value}-session-{sessionNumber}");
            return Task.FromResult(LastSession);
        }

        public Task<IAgentSession> ResumeSessionAsync(
            string sessionId,
            AgentSessionResumeOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentNullException.ThrowIfNull(options);
            LastCreateOptions = options;
            LastSession = CreateBlockingSessions
                ? new BlockingSession(BackendId, sessionId)
                : new ImmediateSession(BackendId, sessionId);
            return Task.FromResult(LastSession);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private abstract class TestSession(AgentBackendId backendId, string sessionId) : IAgentSession
    {
        private readonly List<AgentEvent> _history = [];
        private readonly List<Action<AgentEvent>> _subscribers = [];
        private readonly object _subscriberLock = new();

        public AgentBackendId BackendId { get; } = backendId;

        public string SessionId { get; } = sessionId;

        public string? WorkspacePath => null;

        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<AgentEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            return new DisposableAction(() =>
            {
                lock (_subscriberLock)
                {
                    _subscribers.Remove(handler);
                }
            });
        }

        public abstract Task<AgentRunId> SendAsync(AgentSendOptions options, CancellationToken cancellationToken = default);

        public virtual Task<AgentRunId> SteerAsync(AgentSteerOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(options.ExpectedRunId ?? new AgentRunId("steer-run"));

        public virtual Task AbortAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public virtual Task CompactAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentEvent>>(_history.ToArray());

        protected void Publish(AgentEvent @event)
        {
            _history.Add(@event);
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

    private sealed class ImmediateSession(AgentBackendId backendId, string sessionId) : TestSession(backendId, sessionId)
    {
        private int _runCounter;

        public override Task<AgentRunId> SendAsync(AgentSendOptions options, CancellationToken cancellationToken = default)
        {
            var runId = new AgentRunId($"{SessionId}-run-{Interlocked.Increment(ref _runCounter)}");
            Publish(new AgentSessionUpdateEvent(BackendId, SessionId, DateTimeOffset.UtcNow, runId, AgentSessionUpdateKind.Idle, null));
            return Task.FromResult(runId);
        }
    }

    private sealed class BlockingSession(AgentBackendId backendId, string sessionId) : TestSession(backendId, sessionId), IAgentCompactionOutcomeProvider
    {
        public string FirstRunId => $"{SessionId}-run-1";

        public string SecondRunId => $"{SessionId}-run-2";

        public TaskCompletionSource<bool> FirstSendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SendCount { get; private set; }

        public int AbortCount { get; private set; }

        public int CompactCount { get; private set; }

        public override async Task<AgentRunId> SendAsync(AgentSendOptions options, CancellationToken cancellationToken = default)
        {
            var count = ++SendCount;
            var runId = new AgentRunId(count == 1 ? FirstRunId : SecondRunId);
            if (count == 1)
            {
                FirstSendStarted.TrySetResult(true);
                await ReleaseFirstSend.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            Publish(new AgentSessionUpdateEvent(BackendId, SessionId, DateTimeOffset.UtcNow, runId, AgentSessionUpdateKind.Idle, null));
            return runId;
        }

        public override Task AbortAsync(CancellationToken cancellationToken = default)
        {
            AbortCount++;
            return Task.CompletedTask;
        }

        public override Task CompactAsync(CancellationToken cancellationToken = default)
        {
            CompactCount++;
            return Task.CompletedTask;
        }

        public Task<AgentCompactionOutcome?> CompactWithOutcomeAsync(CancellationToken cancellationToken = default)
        {
            CompactCount++;
            return Task.FromResult<AgentCompactionOutcome?>(new AgentCompactionOutcome(true, "Compacted."));
        }
    }

    private sealed class DisposableAction(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dispose();
        }
    }
}
