using CodeAlta.Agent;
using CodeAlta.App.Context;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Presentation.Threads;

namespace CodeAlta.App;

internal sealed class ThreadInfoService
{
    private readonly record struct ThreadInfoSnapshot(
        SessionViewDescriptor Thread,
        string ProviderDisplayName,
        string? ModelId,
        AgentReasoningEffort? ReasoningEffort,
        IReadOnlyList<AgentEvent>? History);

    private readonly IAgentSessionCatalog _sessionCatalog;
    private readonly ThreadSelectionContext _threadSelection;
    private readonly IReadOnlyDictionary<string, ModelProviderState> _modelProviderStates;

    public ThreadInfoService(
        IAgentSessionCatalog sessionCatalog,
        ThreadSelectionContext threadSelection,
        IReadOnlyDictionary<string, ModelProviderState> modelProviderStates)
    {
        ArgumentNullException.ThrowIfNull(sessionCatalog);
        ArgumentNullException.ThrowIfNull(threadSelection);
        ArgumentNullException.ThrowIfNull(modelProviderStates);

        _sessionCatalog = sessionCatalog;
        _threadSelection = threadSelection;
        _modelProviderStates = modelProviderStates;
    }

    public async Task<ThreadInfoReport?> LoadSelectedThreadReportAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureSelectedThreadSnapshotAsync(cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        AgentSessionMetadata? metadata = null;
        try
        {
            await foreach (var session in _sessionCatalog
                .ListSessionsAsync(filter: null, cancellationToken: cancellationToken))
            {
                if (string.Equals(session.SessionId, snapshot.Value.Thread.ThreadId, StringComparison.Ordinal))
                {
                    metadata = session;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            metadata = null;
        }

        return ThreadInfoReportBuilder.Build(
            snapshot.Value.Thread,
            snapshot.Value.ProviderDisplayName,
            snapshot.Value.ModelId,
            snapshot.Value.ReasoningEffort,
            metadata,
            snapshot.Value.History,
            DateTimeOffset.Now);
    }

    private async Task<ThreadInfoSnapshot?> CaptureSelectedThreadSnapshotAsync(CancellationToken cancellationToken)
    {
        var thread = _threadSelection.GetSelectedThread();
        if (thread is null)
        {
            return null;
        }

        var tab = _threadSelection.EnsureThreadTab(thread);
        var backendState = _modelProviderStates.TryGetValue(thread.ProviderId, out var resolvedBackendState)
            ? resolvedBackendState
            : new ModelProviderState(new ModelProviderId(thread.ProviderId), thread.ProviderId);

        if (!tab.HistoryLoaded || tab.HistoryEvents is null)
        {
            try
            {
                // Thread/tab history lives in UI-owned state, so capture it before switching to
                // background backend I/O for the session lookup below.
                await _threadSelection.EnsureThreadHistoryLoadedAsync(thread, cancellationToken);
            }
            catch (InvalidOperationException)
            {
            }
        }

        return new ThreadInfoSnapshot(
            thread,
            backendState.DisplayName,
            tab.ModelId ?? backendState.SelectedModelId,
            tab.ReasoningEffort,
            tab.HistoryEvents);
    }
}
