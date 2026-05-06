using CodeAlta.Catalog;

namespace CodeAlta.App;

internal sealed class ShellCatalogStateCoordinator
{
    internal readonly record struct CatalogRecoveryResult(string? RestoredThreadId);

    private readonly ProjectCatalog _projectCatalog;
    private readonly WorkThreadCatalog _threadCatalog;
    private readonly ThreadViewStateCoordinator _viewStateCoordinator;
    private readonly OpenThreadStateStore _OpenThreadStateStore;
    private IReadOnlyList<ProjectDescriptor> _projects = [];
    private IReadOnlyList<WorkThreadDescriptor> _threads = [];

    public ShellCatalogStateCoordinator(
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        ThreadViewStateCoordinator viewStateCoordinator,
        OpenThreadStateStore OpenThreadStateStore)
    {
        ArgumentNullException.ThrowIfNull(projectCatalog);
        ArgumentNullException.ThrowIfNull(threadCatalog);
        ArgumentNullException.ThrowIfNull(viewStateCoordinator);
        ArgumentNullException.ThrowIfNull(OpenThreadStateStore);

        _projectCatalog = projectCatalog;
        _threadCatalog = threadCatalog;
        _viewStateCoordinator = viewStateCoordinator;
        _OpenThreadStateStore = OpenThreadStateStore;
    }

    public IReadOnlyList<ProjectDescriptor> Projects => _projects;

    public IReadOnlyList<WorkThreadDescriptor> Threads => _threads;

    public async Task<ShellThreadStateCoordinator.InitialCatalogState> LoadInitialCatalogStateAsync(CancellationToken cancellationToken)
    {
        var projects = await _projectCatalog.LoadAsync(cancellationToken).ConfigureAwait(false);
        var threads = await _threadCatalog.LoadInternalAsync(cancellationToken).ConfigureAwait(false);
        var viewState = await _viewStateCoordinator.LoadViewStateAsync(cancellationToken).ConfigureAwait(false);
        _viewStateCoordinator.ApplyThreadLocalState(threads, viewState);
        return new ShellThreadStateCoordinator.InitialCatalogState(projects, threads, viewState);
    }

    public void ApplyInitialCatalogState(ShellThreadStateCoordinator.InitialCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _projects = state.Projects;
        _threads = state.Threads;
    }

    public CatalogRecoveryResult ApplyRecoveredCatalogState(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads,
        WorkThreadViewState viewState,
        string? pendingStartupThreadRestoreId,
        bool pruneMissingThreads = true)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(viewState);

        _projects = projects;
        _threads = _viewStateCoordinator.ApplyThreadLocalState(threads, viewState);
        if (pruneMissingThreads)
        {
            _OpenThreadStateStore.PruneRetainedThreadState(_threads);
        }
        viewState.Selection ??= WorkThreadSelectionState.GlobalDraft();

        if (pruneMissingThreads)
        {
            viewState.OpenThreadIds.RemoveAll(id => _threads.All(thread => !string.Equals(thread.ThreadId, id, StringComparison.OrdinalIgnoreCase)));
        }
        if (viewState.Selection.Surface == WorkThreadSelectionSurface.Thread &&
            (!viewState.OpenThreadIds.Contains(viewState.Selection.ThreadId, StringComparer.OrdinalIgnoreCase) ||
             FindThread(viewState.Selection.ThreadId) is null))
        {
            viewState.Selection = viewState.Selection.ProjectId is { Length: > 0 } projectId
                ? WorkThreadSelectionState.ProjectDraft(projectId)
                : WorkThreadSelectionState.GlobalDraft(viewState.Selection.ProjectId);
            viewState.SelectedThreadId = null;
        }
        else
        {
            viewState.SelectedThreadId = viewState.Selection.Surface == WorkThreadSelectionSurface.Thread
                ? viewState.Selection.ThreadId
                : null;
        }

        string? restoredThreadId = null;
        if (viewState.Selection.Surface != WorkThreadSelectionSurface.Thread &&
            !string.IsNullOrWhiteSpace(pendingStartupThreadRestoreId) &&
            FindThread(pendingStartupThreadRestoreId) is { } restoredThread)
        {
            if (!viewState.OpenThreadIds.Contains(restoredThread.ThreadId, StringComparer.OrdinalIgnoreCase))
            {
                viewState.OpenThreadIds.Insert(0, restoredThread.ThreadId);
            }

            viewState.Selection = WorkThreadSelectionState.Thread(restoredThread.ThreadId, restoredThread.ProjectRef);
            viewState.SelectedThreadId = restoredThread.ThreadId;
            restoredThreadId = restoredThread.ThreadId;
        }

        return new CatalogRecoveryResult(restoredThreadId);
    }

    public void UpsertThread(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        _threads = _threads
            .Where(existing => !string.Equals(existing.ThreadId, thread.ThreadId, StringComparison.OrdinalIgnoreCase))
            .Append(thread)
            .OrderByDescending(static item => item.LastActiveAt)
            .ToArray();
    }

    public void UpsertProject(ProjectDescriptor project)
    {
        ArgumentNullException.ThrowIfNull(project);

        _projects = _projects
            .Where(existing => !string.Equals(existing.Id, project.Id, StringComparison.OrdinalIgnoreCase))
            .Append(project)
            .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ProjectDescriptor? GetProjectById(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        return _projects.FirstOrDefault(project => string.Equals(project.Id, projectId, StringComparison.OrdinalIgnoreCase));
    }

    public WorkThreadDescriptor? FindThread(string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        return _threads.FirstOrDefault(thread => string.Equals(thread.ThreadId, threadId, StringComparison.OrdinalIgnoreCase));
    }
}
