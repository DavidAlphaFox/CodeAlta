using CodeAlta.Agent;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class AgentHubTests
{
    [TestMethod]
    public async Task ListModelsAsync_CancelledCallerDoesNotPoisonSharedBackendInitialization()
    {
        var backend = new BlockingBackend("local");
        var factory = new AgentBackendFactory();
        factory.Register("local", () => backend);
        await using var hub = new AgentHub(factory);
        using var cts = new CancellationTokenSource();

        var cancelledCaller = hub.ListModelsAsync(new AgentBackendId("local"), cts.Token);
        await backend.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondCaller = hub.ListModelsAsync(new AgentBackendId("local"), CancellationToken.None);

        await cts.CancelAsync();
        try
        {
            _ = await cancelledCaller;
            Assert.Fail("The cancelled caller should observe cancellation.");
        }
        catch (OperationCanceledException)
        {
        }

        backend.AllowStart.SetResult();
        var models = await secondCaller;

        Assert.AreEqual(1, models.Count);
        Assert.AreEqual("test-model", models[0].Id);
        Assert.AreEqual(1, backend.StartCallCount);
        Assert.IsFalse(backend.Disposed);
    }

    private sealed class BlockingBackend(string backendId) : IAgentBackend
    {
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCallCount { get; private set; }

        public bool Disposed { get; private set; }

        public AgentBackendId BackendId { get; } = new(backendId);

        public string DisplayName => "Blocking";

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            StartEntered.TrySetResult();
            await AllowStart.Task.WaitAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentModelInfo>>([new AgentModelInfo("test-model", "Test Model")]);

        public Task<IReadOnlyList<AgentSessionMetadata>> ListSessionsAsync(
            AgentSessionListFilter? filter = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentSessionMetadata>>([]);

        public Task<IAgentSession> CreateSessionAsync(
            AgentSessionCreateOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IAgentSession> ResumeSessionAsync(
            string sessionId,
            AgentSessionResumeOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
