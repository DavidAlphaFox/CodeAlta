using CodeAlta.Catalog;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal sealed class ThreadViewStateCoordinator
{
    private readonly WorkThreadCatalog _threadCatalog;

    public ThreadViewStateCoordinator(WorkThreadCatalog threadCatalog)
    {
        ArgumentNullException.ThrowIfNull(threadCatalog);
        _threadCatalog = threadCatalog;
    }

    public Task<WorkThreadViewState> LoadViewStateAsync(CancellationToken cancellationToken)
        => _threadCatalog.LoadViewStateAsync(cancellationToken);

    public async Task PersistViewStateAsync(WorkThreadViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(viewState);

        try
        {
            await _threadCatalog.SaveViewStateAsync(viewState, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (LogManager.IsInitialized && CodeAlta.Views.CodeAltaApp.UiLogger.IsEnabled(LogLevel.Error))
            {
                CodeAlta.Views.CodeAltaApp.UiLogger.Error(ex, "Failed to persist thread view state.");
            }
        }
    }

    public IReadOnlyList<WorkThreadDescriptor> ApplyThreadLocalState(
        IReadOnlyList<WorkThreadDescriptor> threads,
        WorkThreadViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(viewState);

        foreach (var thread in threads)
        {
            if (!viewState.ThreadStates.TryGetValue(thread.ThreadId, out var localState))
            {
                continue;
            }

            if (localState.Archived)
            {
                thread.Status = WorkThreadStatus.Archived;
            }

            if (localState.MessageCount is { } messageCount)
            {
                thread.MessageCount = messageCount;
            }
        }

        return threads;
    }

    public async Task PersistThreadLocalStateAsync(WorkThreadViewState viewState, WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        ArgumentNullException.ThrowIfNull(thread);

        viewState.ThreadStates[thread.ThreadId] = new WorkThreadLocalState
        {
            Archived = thread.Status == WorkThreadStatus.Archived,
            MessageCount = thread.MessageCount,
        };
        viewState.UpdatedAt = DateTimeOffset.UtcNow;
        await PersistViewStateAsync(viewState).ConfigureAwait(false);
    }

    public NavigatorSettings GetNavigatorSettingsSnapshot(WorkThreadViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(viewState);

        return new NavigatorSettings
        {
            SortMode = viewState.Navigator.SortMode,
            RecentThreadsPerProject = viewState.Navigator.RecentThreadsPerProject,
        };
    }

    public async Task SaveNavigatorSettingsAsync(WorkThreadViewState viewState, NavigatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        viewState.Navigator = new NavigatorSettings
        {
            SortMode = settings.SortMode,
            RecentThreadsPerProject = settings.RecentThreadsPerProject,
        };
        viewState.UpdatedAt = DateTimeOffset.UtcNow;
        await PersistViewStateAsync(viewState).ConfigureAwait(false);
    }
}
