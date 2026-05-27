using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.App.Events;
using CodeAlta.Models;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ModelProviderInitializationCoordinatorTests
{
    [TestMethod]
    public async Task InitializeAsync_RefreshesLoadedNonProcessBackedBackend()
    {
        var ProviderId = new ModelProviderId("openai");
        var backend = new CountingBackend(ProviderId);
        var descriptor = new ModelProviderDescriptor(ProviderId, "OpenAI");
        var initializationService = CreateInitializationService(descriptor, backend);
        var state = new ModelProviderState(new ModelProviderId(ProviderId.Value), "OpenAI")
        {
            Availability = ModelProviderAvailability.Ready,
            StatusMessage = "Ready",
        };
        state.Models.Add(new AgentModelInfo("old-model"));

        var coordinator = CreateCoordinator(
            initializationService,
            [descriptor],
            new Dictionary<string, ModelProviderState>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderId.Value] = state,
            });

        await coordinator.InitializeAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, backend.StartCount);
        Assert.AreEqual(1, backend.ListModelsCount);
        Assert.AreEqual(ModelProviderAvailability.Ready, state.Availability);
        CollectionAssert.AreEqual(new[] { "new-model" }, state.Models.Select(static model => model.Id).ToArray());
    }

    [TestMethod]
    public async Task RefreshProviderAsync_PreservesSelectedModelWhenMissingFromDiscoveredModels()
    {
        var ProviderId = new ModelProviderId("codex");
        var backend = new CountingBackend(
            ProviderId,
            [
                new AgentModelInfo(
                    "gpt-5.2",
                    SupportedReasoningEfforts: [AgentReasoningEffort.Low, AgentReasoningEffort.Medium]),
            ]);
        var descriptor = new ModelProviderDescriptor(ProviderId, "Codex");
        var initializationService = CreateInitializationService(descriptor, backend);
        var state = new ModelProviderState(new ModelProviderId(ProviderId.Value), "Codex")
        {
            SelectedModelId = "gpt-5.5",
            SelectedReasoningEffort = AgentReasoningEffort.High,
        };
        var coordinator = CreateCoordinator(
            initializationService,
            [descriptor],
            new Dictionary<string, ModelProviderState>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderId.Value] = state,
            });

        await coordinator.RefreshProviderAsync(new ModelProviderId(ProviderId.Value), CancellationToken.None).ConfigureAwait(false);

        CollectionAssert.AreEqual(new[] { "gpt-5.2" }, state.Models.Select(static model => model.Id).ToArray());
        Assert.AreEqual("gpt-5.5", state.SelectedModelId);
        Assert.AreEqual(AgentReasoningEffort.High, state.SelectedReasoningEffort);
    }

    [TestMethod]
    public async Task RefreshProviderAsync_UpdatesUiStateAfterProviderRefresh()
    {
        var ProviderId = new ModelProviderId("codex");
        var backend = new CountingBackend(ProviderId);
        var descriptor = new ModelProviderDescriptor(ProviderId, "ChatGPT");
        var initializationService = CreateInitializationService(descriptor, backend);
        var state = new ModelProviderState(new ModelProviderId(ProviderId.Value), "ChatGPT");
        var queuedUiActions = new Queue<Action>();
        var publishedEvents = new List<ShellFrontendEvent>();
        var coordinator = new ModelProviderInitializationCoordinator(
            initializationService,
            [descriptor],
            new Dictionary<string, ModelProviderState>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderId.Value] = state,
            },
            action => queuedUiActions.Enqueue(action),
            CreatePublisher(publishedEvents));

        await coordinator.RefreshProviderAsync(new ModelProviderId(ProviderId.Value), CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ModelProviderAvailability.Unknown, state.Availability);

        while (queuedUiActions.TryDequeue(out var action))
        {
            action();
        }

        Assert.AreEqual(ModelProviderAvailability.Ready, state.Availability);
        Assert.IsTrue(publishedEvents.OfType<ModelProviderStateChangedEvent>().Any(evt => evt.ModelProviderId == ProviderId.Value));
        Assert.IsTrue(publishedEvents.OfType<HeaderChangedEvent>().Any());
    }

    [TestMethod]
    public async Task InitializeAsync_CreatesMissingBackendStateBeforeRefreshingModels()
    {
        var ProviderId = new ModelProviderId("gemini");
        var backend = new CountingBackend(ProviderId);
        var descriptor = new ModelProviderDescriptor(ProviderId, "Gemini");
        var initializationService = CreateInitializationService(descriptor, backend);
        var states = new Dictionary<string, ModelProviderState>(StringComparer.OrdinalIgnoreCase);
        var coordinator = CreateCoordinator(
            initializationService,
            [descriptor],
            states);

        await coordinator.InitializeAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(states.TryGetValue(ProviderId.Value, out var state));
        Assert.AreEqual("Gemini", state.DisplayName);
        Assert.AreEqual(1, backend.ListModelsCount);
        Assert.AreEqual(ModelProviderAvailability.Ready, state.Availability);
        CollectionAssert.AreEqual(new[] { "new-model" }, state.Models.Select(static model => model.Id).ToArray());
    }

    [TestMethod]
    public async Task InitializeAsync_DropsStaleQueuedProviderInitializationStatus()
    {
        var ProviderId = new ModelProviderId("openai");
        var descriptor = new ModelProviderDescriptor(ProviderId, "OpenAI");
        var initializationService = CreateInitializationService(descriptor, new CountingBackend(ProviderId));
        var state = new ModelProviderState(new ModelProviderId(ProviderId.Value), "OpenAI");
        var queuedUiActions = new List<Action>();
        var providerStatuses = new List<string?>();
        var coordinator = new ModelProviderInitializationCoordinator(
            initializationService,
            [descriptor],
            new Dictionary<string, ModelProviderState>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderId.Value] = state,
            },
            action => queuedUiActions.Add(action),
            CreatePublisher(),
            setProviderInitializationStatus: providerStatuses.Add);

        await coordinator.InitializeAsync(CancellationToken.None).ConfigureAwait(false);

        foreach (var action in queuedUiActions.AsEnumerable().Reverse())
        {
            action();
        }

        Assert.IsTrue(providerStatuses.Count > 0);
        Assert.IsNull(providerStatuses.Last());
        Assert.IsFalse(
            providerStatuses.SkipWhile(static status => status is not null).Skip(1).Any(static status => status is not null),
            "Older queued provider-loading statuses must not overwrite the final cleared status.");
    }

    [TestMethod]
    public void FormatProviderInitializationStatus_ShowsProgressAndProviderNames()
    {
        var status = ModelProviderInitializationCoordinator.FormatProviderInitializationStatus(
            1,
            3,
            ["OpenAI", "Gemma", "Anthropic"]);

        Assert.AreEqual("Initializing OpenAI, Gemma, … [■■■□□□□□] 1/3", status);
    }

    [TestMethod]
    public void FormatProviderInitializationStatus_HidesWhenComplete()
    {
        var status = ModelProviderInitializationCoordinator.FormatProviderInitializationStatus(
            3,
            3,
            []);

        Assert.IsNull(status);
    }

    private static ModelProviderInitializationCoordinator CreateCoordinator(
        IModelProviderInitializationService initializationService,
        IReadOnlyList<ModelProviderDescriptor> descriptors,
        Dictionary<string, ModelProviderState> states)
        => new(
            initializationService,
            descriptors,
            states,
            static action => action(),
            CreatePublisher());

    private static ModelProviderInitializationService CreateInitializationService(
        ModelProviderDescriptor descriptor,
        CountingBackend backend)
    {
        var registry = new ModelProviderRegistry();
        registry.RegisterOrReplaceBackendRuntime(descriptor, () => backend);
        return new ModelProviderInitializationService(registry);
    }

    private static FrontendEventPublisher CreatePublisher(List<ShellFrontendEvent>? events = null)
    {
        var publisher = new FrontendEventPublisher(new InlineUiDispatcher());
        if (events is not null)
        {
            publisher.Subscribe(events.Add);
        }

        return publisher;
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
            => Task.FromResult(action());
    }

    private sealed class CountingBackend : ITestModelProviderSessionRuntime
    {
        private readonly IReadOnlyList<AgentModelInfo> _models;

        public CountingBackend(ModelProviderId providerId, IReadOnlyList<AgentModelInfo>? models = null)
        {
            ProviderId = providerId;
            _models = models ?? [new AgentModelInfo("new-model")];
        }

        public ModelProviderId ProviderId { get; }

        public string DisplayName => ProviderId.Value;

        public int StartCount { get; private set; }

        public int ListModelsCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            ListModelsCount++;
            return Task.FromResult(_models);
        }

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
            => throw new NotSupportedException();

        public Task<IAgentSession> ResumeSessionAsync(
            string sessionId,
            AgentSessionResumeOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TempDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codealta-chatbackend-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
