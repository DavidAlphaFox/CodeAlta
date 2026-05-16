using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.App;

internal sealed class RecoverableThreadSource : IRecoverableThreadSource
{
    private readonly WorkThreadRuntimeService _runtimeService;

    public RecoverableThreadSource(WorkThreadRuntimeService runtimeService)
    {
        ArgumentNullException.ThrowIfNull(runtimeService);
        _runtimeService = runtimeService;
    }

    public Func<AgentBackendId, bool>? ShouldListBackendSessions { get; set; }

    public IAsyncEnumerable<WorkThreadDescriptor> StreamRecoverableThreadsAsync(CancellationToken cancellationToken)
        => _runtimeService.StreamRecoverableThreadsAsync(cancellationToken: cancellationToken);

    public IAsyncEnumerable<WorkThreadDescriptor> StreamRecoverableThreadsAsync(
        Func<AgentBackendId, bool>? shouldListBackendSessions,
        CancellationToken cancellationToken)
        => _runtimeService.StreamRecoverableThreadsAsync(
            backendId => (ShouldListBackendSessions?.Invoke(backendId) ?? true) &&
                         (shouldListBackendSessions?.Invoke(backendId) ?? true),
            cancellationToken);

    public Task<IReadOnlyList<WorkThreadDescriptor>> ListRecoverableThreadsAsync(CancellationToken cancellationToken)
        => _runtimeService.ListRecoverableThreadsAsync(cancellationToken: cancellationToken);

    public Task<IReadOnlyList<WorkThreadDescriptor>> ListRecoverableThreadsAsync(
        Func<AgentBackendId, bool>? shouldListBackendSessions,
        CancellationToken cancellationToken)
        => _runtimeService.ListRecoverableThreadsAsync(
            backendId => (ShouldListBackendSessions?.Invoke(backendId) ?? true) &&
                         (shouldListBackendSessions?.Invoke(backendId) ?? true),
            cancellationToken);
}
