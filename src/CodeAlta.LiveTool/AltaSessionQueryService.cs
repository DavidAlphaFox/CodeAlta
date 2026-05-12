using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.LiveTool;

internal interface IAltaSessionQueryService
{
    Task<IReadOnlyList<AltaSessionInfo>?> LoadAsync(AltaCommandContext context);
}

internal sealed class AltaSessionQueryService : IAltaSessionQueryService
{
    public async Task<IReadOnlyList<AltaSessionInfo>?> LoadAsync(AltaCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtime = context.Services.Get<WorkThreadRuntimeService>();
        var threadCatalog = context.Services.Get<WorkThreadCatalog>();
        IReadOnlyList<WorkThreadDescriptor> threads;
        if (runtime is not null)
        {
            threads = await runtime.ListRecoverableThreadsAsync(context.CancellationToken).ConfigureAwait(false);
        }
        else if (threadCatalog is not null)
        {
            threads = await threadCatalog.LoadInternalAsync(context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            AltaJsonlWriter.WriteError(
                context.Stderr,
                context.CorrelationId,
                "service.unavailable",
                AltaExitCodes.ServiceUnavailable,
                $"Required in-process service '{nameof(WorkThreadRuntimeService)}' or '{nameof(WorkThreadCatalog)}' is unavailable.");
            return null;
        }

        WorkThreadViewState? viewState = null;
        if (threadCatalog is not null)
        {
            try
            {
                viewState = await threadCatalog.LoadViewStateAsync(context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                AltaJsonlWriter.WriteWarning(context.Stderr, context.CorrelationId, "thread.viewStateUnavailable", ex.Message);
            }
        }

        var results = new List<AltaSessionInfo>(threads.Count);
        foreach (var thread in threads)
        {
            var localState = await TryGetLocalStateAsync(threadCatalog, viewState, thread, context.CancellationToken).ConfigureAwait(false);
            var preference = TryGetPreference(viewState, thread.ThreadId);
            var isRunning = runtime is not null && await runtime.HasActiveRunAsync(thread, context.CancellationToken).ConfigureAwait(false);
            var hasActiveSession = isRunning || (runtime is not null && await runtime.HasActiveCoordinatorSessionAsync(thread.ThreadId, context.CancellationToken).ConfigureAwait(false));
            if (localState?.Archived == true)
            {
                thread.Status = WorkThreadStatus.Archived;
            }

            if (localState?.MessageCount is not null)
            {
                thread.MessageCount = localState.MessageCount;
            }

            if (!string.IsNullOrWhiteSpace(localState?.ParentThreadId))
            {
                thread.ParentThreadId = localState.ParentThreadId;
            }

            if (localState?.CreatedBy is not null)
            {
                thread.CreatedBy = localState.CreatedBy;
            }

            if (!string.IsNullOrWhiteSpace(localState?.ProviderKey))
            {
                thread.ProviderKey = localState.ProviderKey;
            }

            if (!string.IsNullOrWhiteSpace(localState?.ModelId))
            {
                thread.ModelId = localState.ModelId;
            }

            if (localState?.ReasoningEffort is not null)
            {
                thread.ReasoningEffort = localState.ReasoningEffort;
            }

            results.Add(new AltaSessionInfo(thread, localState, preference, isRunning, hasActiveSession, ResolveState(thread, isRunning, hasActiveSession)));
        }

        return results;
    }

    private static async Task<WorkThreadLocalState?> TryGetLocalStateAsync(
        WorkThreadCatalog? threadCatalog,
        WorkThreadViewState? viewState,
        WorkThreadDescriptor thread,
        CancellationToken cancellationToken)
    {
        if (threadCatalog is not null)
        {
            try
            {
                var journalState = await threadCatalog.JournalStore
                    .ReadLatestStateAsync(thread.ThreadId, thread.CreatedAt, cancellationToken)
                    .ConfigureAwait(false);
                if (journalState is not null)
                {
                    return journalState;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            {
            }
        }

        return viewState is not null && viewState.ThreadStates.TryGetValue(thread.ThreadId, out var state) ? state : null;
    }

    private static WorkThreadPreference? TryGetPreference(WorkThreadViewState? viewState, string threadId)
        => viewState is not null && viewState.ThreadPreferences.TryGetValue(threadId, out var preference) ? preference : null;

    private static string ResolveState(WorkThreadDescriptor thread, bool isRunning, bool hasActiveSession)
    {
        if (thread.Status == WorkThreadStatus.Archived)
        {
            return "archived";
        }

        if (isRunning)
        {
            return "running";
        }

        return hasActiveSession ? "idle" : "inactive";
    }
}

internal sealed record AltaSessionInfo(
    WorkThreadDescriptor Thread,
    WorkThreadLocalState? LocalState,
    WorkThreadPreference? Preference,
    bool IsRunning,
    bool HasActiveSession,
    string State);
