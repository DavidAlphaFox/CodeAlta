using CodeAlta.Agent;
using CodeAlta.Agent.LocalRuntime;

namespace CodeAlta.Tests;

internal static class ModelProviderRuntimeTestExtensions
{
    public static void RegisterOrReplaceBackendRuntime(
        this ModelProviderRegistry registry,
        ModelProviderDescriptor descriptor,
        Func<ITestModelProviderSessionRuntime> createBackend)
    {
        registry.RegisterOrReplace(descriptor, () => new LegacyBackendRuntime(descriptor, createBackend()));
    }

    public static async Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(
        this IModelProviderRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        var probe = await runtime.ProbeAsync(cancellationToken).ConfigureAwait(false);
        return probe.Models;
    }

    public static async Task<IAgentSession> CreateSessionAsync(
        this ICodeAltaModelProviderRuntime runtime,
        AgentSessionCreateOptions options,
        CancellationToken cancellationToken = default)
    {
        var sessionRuntime = CreateSessionRuntime(runtime, options.WorkingDirectory);
        await TryPrimeModelCacheAsync(sessionRuntime, cancellationToken).ConfigureAwait(false);
        return await sessionRuntime.CreateSessionAsync(options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IAgentSession> ResumeSessionAsync(
        this ICodeAltaModelProviderRuntime runtime,
        string sessionId,
        AgentSessionResumeOptions options,
        CancellationToken cancellationToken = default)
    {
        var stateRootPath = options.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(stateRootPath))
        {
            stateRootPath = FindStateRootForSession(sessionId);
        }

        var sessionRuntime = CreateSessionRuntime(runtime, stateRootPath);
        await TryPrimeModelCacheAsync(sessionRuntime, cancellationToken).ConfigureAwait(false);
        return await sessionRuntime.ResumeSessionAsync(sessionId, options, cancellationToken).ConfigureAwait(false);
    }

    private static CodeAltaAgentRuntime CreateSessionRuntime(ICodeAltaModelProviderRuntime runtime, string? stateRootPath)
        => new(
            runtime.Descriptor.ProviderId,
            runtime.Descriptor.DisplayName,
            new CodeAltaAgentRuntimeOptions
            {
                StateRootPath = stateRootPath,
                Providers = [runtime.CreateProviderRegistration()],
            });

    private static async Task TryPrimeModelCacheAsync(CodeAltaAgentRuntime runtime, CancellationToken cancellationToken)
    {
        try
        {
            await runtime.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
        }
    }

    private static string? FindStateRootForSession(string sessionId)
    {
        foreach (var sessionsDirectory in Directory.EnumerateDirectories(AppContext.BaseDirectory, "sessions", SearchOption.AllDirectories))
        {
            if (Directory.Exists(Path.Combine(sessionsDirectory, sessionId)))
            {
                return Directory.GetParent(sessionsDirectory)?.FullName;
            }
        }

        return null;
    }

    private sealed class LegacyBackendRuntime : IModelProviderSessionRuntime
    {
        private readonly ITestModelProviderSessionRuntime _backend;

        public LegacyBackendRuntime(ModelProviderDescriptor descriptor, ITestModelProviderSessionRuntime backend)
        {
            Descriptor = descriptor;
            _backend = backend;
        }

        public ModelProviderDescriptor Descriptor { get; }

        public Task StartAsync(CancellationToken cancellationToken = default) => _backend.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) => _backend.StopAsync(cancellationToken);

        public async Task<ModelProviderProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
        {
            var models = await _backend.ListModelsAsync(cancellationToken).ConfigureAwait(false);
            return new ModelProviderProbeResult
            {
                ProviderId = Descriptor.ProviderId,
                Availability = ModelProviderAvailability.Ready,
                Models = models,
            };
        }

        public IModelProviderTurnExecutor CreateTurnExecutor()
            => throw new NotSupportedException();

        public Task<IAgentSession> CreateSessionAsync(AgentSessionCreateOptions options, CancellationToken cancellationToken = default)
            => _backend.CreateSessionAsync(options, cancellationToken);

        public Task<IAgentSession> ResumeSessionAsync(string sessionId, AgentSessionResumeOptions options, CancellationToken cancellationToken = default)
            => _backend.ResumeSessionAsync(sessionId, options, cancellationToken);

        public ValueTask DisposeAsync() => _backend.DisposeAsync();
    }
}
