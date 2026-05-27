using CodeAlta.Agent;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class AgentHubTests
{
    [TestMethod]
    public async Task StartSessionAsync_DoesNotProbeModelsOrListSessions()
    {
        var backend = new CountingBackend("provider");
        var factory = new AgentBackendFactory();
        factory.Register("provider", () => backend);
        await using var hub = new AgentHub(factory);

        var handle = await hub.StartSessionAsync(
                new AgentSessionCreateOptions
                {
                    ProviderKey = "provider",
                    WorkingDirectory = Environment.CurrentDirectory,
                    OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                })
            .ConfigureAwait(false);

        Assert.AreEqual("provider-session", handle.SessionId);
        Assert.AreEqual(1, backend.StartCallCount);
        Assert.AreEqual(1, backend.CreateSessionCallCount);
        Assert.AreEqual(0, backend.ListModelsCallCount);
        Assert.AreEqual(0, backend.ListSessionsCallCount);
    }

    private sealed class CountingBackend(string backendId) : IAgentBackend
    {
        public int StartCallCount { get; private set; }

        public int CreateSessionCallCount { get; private set; }

        public int ListModelsCallCount { get; private set; }

        public int ListSessionsCallCount { get; private set; }

        public AgentBackendId BackendId { get; } = new(backendId);

        public string DisplayName => "Counting";

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            ListModelsCallCount++;
            return Task.FromResult<IReadOnlyList<AgentModelInfo>>([new AgentModelInfo("test-model", "Test Model")]);
        }

        public async IAsyncEnumerable<AgentSessionMetadata> ListSessionsAsync(
            AgentSessionListFilter? filter = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ListSessionsCallCount++;
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
            CreateSessionCallCount++;
            return Task.FromResult<IAgentSession>(new CountingSession(BackendId));
        }

        public Task<IAgentSession> ResumeSessionAsync(
            string sessionId,
            AgentSessionResumeOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IAgentSession>(new CountingSession(BackendId, sessionId));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingSession(AgentBackendId backendId, string sessionId = "provider-session") : IAgentSession
    {
        public AgentBackendId BackendId { get; } = backendId;

        public string SessionId { get; } = sessionId;

        public string? WorkspacePath => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async IAsyncEnumerable<AgentEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public IDisposable Subscribe(Action<AgentEvent> handler) => new Subscription();

        public Task<AgentRunId> SendAsync(AgentSendOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRunId("run"));

        public Task<AgentRunId> SteerAsync(AgentSteerOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRunId("run"));

        public Task AbortAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CompactAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentEvent>>([]);
    }

    private sealed class Subscription : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
