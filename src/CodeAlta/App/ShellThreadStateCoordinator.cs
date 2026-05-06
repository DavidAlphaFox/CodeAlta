using CodeAlta.App.State;
using CodeAlta.Threading;
using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Shell;
using CodeAlta.Views;
using XenoAtom.Terminal.UI.Geometry;

namespace CodeAlta.App;

internal sealed class ShellThreadStateCoordinator
{
    internal sealed record InitialCatalogState(
        IReadOnlyList<ProjectDescriptor> Projects,
        IReadOnlyList<WorkThreadDescriptor> Threads,
        WorkThreadViewState ViewState);

    private readonly ShellSelectionCoordinator _selectionCoordinator = new();
    private readonly Func<IUiDispatcher> _getUiDispatcher;
    private readonly ShellCatalogStateCoordinator _catalogStateCoordinator;
    private readonly OpenThreadStateStore _OpenThreadStateStore;
    private readonly ThreadViewStateCoordinator _viewStateCoordinator;
    private readonly Func<WorkThreadDescriptor, bool> _isBackendReady;
    private readonly Action<string> _deletePromptDraft;
    private readonly Func<WorkThreadDescriptor, CancellationToken, Task> _ensureThreadHistoryLoadedAsync;
    private readonly Action _refreshSelectionAndThreadWorkspace;
    private readonly Action _refreshCatalogAndThreadWorkspace;
    private readonly Action _resetPendingThreadTabSelection;
    private readonly Action<string> _removeTabPage;
    private readonly Action<string, bool, StatusTone> _setStatus;
    public ShellThreadStateCoordinator(
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        Func<IUiDispatcher> getUiDispatcher,
        Func<Rectangle?> getTimelineBounds,
        Func<WorkThreadDescriptor, bool> isBackendReady,
        Func<string, string?> loadPromptDraft,
        Action<string> deletePromptDraft,
        Action<OpenThreadState> applyThreadPreference,
        Action<string, string?, AgentReasoningEffort?, bool> rememberThreadPreference,
        Func<WorkThreadDescriptor, CancellationToken, Task> ensureThreadHistoryLoadedAsync,
        Action refreshSelectionAndThreadWorkspace,
        Action refreshCatalogAndThreadWorkspace,
        Action resetPendingThreadTabSelection,
        Action<string> removeTabPage,
        Action<string, bool, StatusTone> setStatus)
    {
        ArgumentNullException.ThrowIfNull(projectCatalog);
        ArgumentNullException.ThrowIfNull(threadCatalog);
        ArgumentNullException.ThrowIfNull(getUiDispatcher);
        ArgumentNullException.ThrowIfNull(getTimelineBounds);
        ArgumentNullException.ThrowIfNull(isBackendReady);
        ArgumentNullException.ThrowIfNull(loadPromptDraft);
        ArgumentNullException.ThrowIfNull(deletePromptDraft);
        ArgumentNullException.ThrowIfNull(applyThreadPreference);
        ArgumentNullException.ThrowIfNull(rememberThreadPreference);
        ArgumentNullException.ThrowIfNull(ensureThreadHistoryLoadedAsync);
        ArgumentNullException.ThrowIfNull(refreshSelectionAndThreadWorkspace);
        ArgumentNullException.ThrowIfNull(refreshCatalogAndThreadWorkspace);
        ArgumentNullException.ThrowIfNull(resetPendingThreadTabSelection);
        ArgumentNullException.ThrowIfNull(removeTabPage);
        ArgumentNullException.ThrowIfNull(setStatus);

        _getUiDispatcher = getUiDispatcher;
        _viewStateCoordinator = new ThreadViewStateCoordinator(threadCatalog);
        _OpenThreadStateStore = new OpenThreadStateStore(
            getUiDispatcher,
            getTimelineBounds,
            loadPromptDraft,
            applyThreadPreference,
            rememberThreadPreference,
            GetSelectedProject,
            GetProjectById);
        _catalogStateCoordinator = new ShellCatalogStateCoordinator(projectCatalog, threadCatalog, _viewStateCoordinator, _OpenThreadStateStore);
        _isBackendReady = isBackendReady;
        _deletePromptDraft = deletePromptDraft;
        _ensureThreadHistoryLoadedAsync = ensureThreadHistoryLoadedAsync;
        _refreshSelectionAndThreadWorkspace = refreshSelectionAndThreadWorkspace;
        _refreshCatalogAndThreadWorkspace = refreshCatalogAndThreadWorkspace;
        _resetPendingThreadTabSelection = resetPendingThreadTabSelection;
        _removeTabPage = removeTabPage;
        _setStatus = setStatus;
    }

    public IReadOnlyList<ProjectDescriptor> Projects => _catalogStateCoordinator.Projects;

    public IReadOnlyList<WorkThreadDescriptor> Threads => _catalogStateCoordinator.Threads;

    public WorkThreadViewState ViewState
    {
        get => _selectionCoordinator.ViewState;
        set => _selectionCoordinator.ViewState = value;
    }

    public bool DraftTabOpen
    {
        get => _selectionCoordinator.DraftTabOpen;
        set => _selectionCoordinator.DraftTabOpen = value;
    }

    public bool GlobalScopeSelected
    {
        get => _selectionCoordinator.GlobalScopeSelected;
        set => _selectionCoordinator.GlobalScopeSelected = value;
    }

    public string? SelectedProjectId
    {
        get => _selectionCoordinator.SelectedProjectId;
        set => _selectionCoordinator.SelectedProjectId = value;
    }

    public string? SelectedThreadId
    {
        get => _selectionCoordinator.SelectedThreadId;
        set => _selectionCoordinator.SelectedThreadId = value;
    }

    public string? PendingStartupThreadRestoreId
    {
        get => _selectionCoordinator.PendingStartupThreadRestoreId;
        set => _selectionCoordinator.PendingStartupThreadRestoreId = value;
    }

    public ShellSelection Selection => _selectionCoordinator.Selection;

    public NavigatorSettings NavigatorSettings => ViewState.Navigator;

    public async Task<InitialCatalogState> LoadInitialCatalogStateAsync(CancellationToken cancellationToken)
    {
        return await _catalogStateCoordinator.LoadInitialCatalogStateAsync(cancellationToken);
    }

    public void ApplyInitialCatalogState(InitialCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _catalogStateCoordinator.ApplyInitialCatalogState(state);
        _selectionCoordinator.ApplyInitialSelection(state.ViewState, Projects, Threads);
    }

    public async Task LoadCatalogStateAsync(CancellationToken cancellationToken)
    {
        ApplyInitialCatalogState(await LoadInitialCatalogStateAsync(cancellationToken));
    }

    public async Task PersistViewStateAsync()
        => await _viewStateCoordinator.PersistViewStateAsync(ViewState);

    public void ApplyRecoveredCatalogState(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads,
        bool pruneMissingThreads = true)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(threads);

        var previousPendingStartupThreadRestoreId = PendingStartupThreadRestoreId;
        var recovery = _catalogStateCoordinator.ApplyRecoveredCatalogState(
            projects,
            threads,
            ViewState,
            PendingStartupThreadRestoreId,
            pruneMissingThreads);

        _selectionCoordinator.ApplyInitialSelection(ViewState, Projects, Threads);
        PendingStartupThreadRestoreId = recovery.RestoredThreadId ??
            (!pruneMissingThreads && !string.IsNullOrWhiteSpace(previousPendingStartupThreadRestoreId)
                ? previousPendingStartupThreadRestoreId
                : PendingStartupThreadRestoreId);

        EnsureSelectionDefaults();
        _refreshCatalogAndThreadWorkspace();
    }

    public async Task PersistThreadLocalStateAsync(WorkThreadDescriptor thread)
        => await _viewStateCoordinator.PersistThreadLocalStateAsync(ViewState, thread);

    public void UpsertProject(ProjectDescriptor project)
    {
        ArgumentNullException.ThrowIfNull(project);

        _catalogStateCoordinator.UpsertProject(project);
    }

    public void RekeyThreadIdentity(string oldThreadId, WorkThreadDescriptor thread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldThreadId);
        ArgumentNullException.ThrowIfNull(thread);

        if (string.Equals(oldThreadId, thread.ThreadId, StringComparison.OrdinalIgnoreCase))
        {
            _catalogStateCoordinator.UpsertThread(thread);
            return;
        }

        _OpenThreadStateStore.RekeyThreadTab(oldThreadId, thread);
        _removeTabPage(oldThreadId);

        for (var index = 0; index < ViewState.OpenThreadIds.Count; index++)
        {
            if (string.Equals(ViewState.OpenThreadIds[index], oldThreadId, StringComparison.OrdinalIgnoreCase))
            {
                ViewState.OpenThreadIds[index] = thread.ThreadId;
            }
        }

        ViewState.OpenThreadIds = ViewState.OpenThreadIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ViewState.ThreadStates.Remove(oldThreadId, out var localState))
        {
            ViewState.ThreadStates[thread.ThreadId] = localState;
        }

        if (ViewState.ThreadPreferences.Remove(oldThreadId, out var preference))
        {
            ViewState.ThreadPreferences[thread.ThreadId] = preference;
        }

        if (string.Equals(PendingStartupThreadRestoreId, oldThreadId, StringComparison.OrdinalIgnoreCase))
        {
            PendingStartupThreadRestoreId = thread.ThreadId;
        }

        if (string.Equals(ViewState.SelectedThreadId, oldThreadId, StringComparison.OrdinalIgnoreCase))
        {
            ViewState.SelectedThreadId = thread.ThreadId;
        }

        if (ViewState.Selection.Surface == WorkThreadSelectionSurface.Thread &&
            string.Equals(ViewState.Selection.ThreadId, oldThreadId, StringComparison.OrdinalIgnoreCase))
        {
            ViewState.Selection = WorkThreadSelectionState.Thread(thread.ThreadId, thread.ProjectRef);
            ViewState.SelectedThreadId = thread.ThreadId;
            SelectedThreadId = thread.ThreadId;
        }

        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        _catalogStateCoordinator.UpsertThread(thread);
    }

    public NavigatorSettings GetNavigatorSettingsSnapshot()
        => _viewStateCoordinator.GetNavigatorSettingsSnapshot(ViewState);

    public async Task SaveNavigatorSettingsAsync(NavigatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        await _viewStateCoordinator.SaveNavigatorSettingsAsync(ViewState, settings);
    }

    public void TrySchedulePendingStartupThreadRestore(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(PendingStartupThreadRestoreId))
        {
            return;
        }

        var thread = FindThread(PendingStartupThreadRestoreId);
        if (thread is null || !_isBackendReady(thread))
        {
            return;
        }

        var threadId = PendingStartupThreadRestoreId;
        PendingStartupThreadRestoreId = null;
        _ = RestoreStartupThreadHistoryAsync(threadId, cancellationToken);
    }

    public void SelectGlobalScope()
    {
        _resetPendingThreadTabSelection();
        _selectionCoordinator.SelectGlobalScope(Projects);
        ViewState.SelectedThreadId = null;
        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        _ = PersistViewStateAsync();
        _refreshSelectionAndThreadWorkspace();
    }

    public void SelectProjectScope(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        _resetPendingThreadTabSelection();
        _selectionCoordinator.SelectProjectScope(projectId, Projects);
        ViewState.SelectedThreadId = null;
        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        _ = PersistViewStateAsync();
        _refreshSelectionAndThreadWorkspace();
    }

    public void EnsureSelectionDefaults()
        => _selectionCoordinator.EnsureSelectionDefaults(Projects, Threads);

    public async Task RegisterCreatedThreadAsync(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        UiDispatch.Invoke(
            _getUiDispatcher(),
            () =>
            {
                _catalogStateCoordinator.UpsertThread(thread);
                OpenThread(thread.ThreadId);
            });

        await _ensureThreadHistoryLoadedAsync(thread, CancellationToken.None);
    }

    public void OpenThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var thread = FindThread(threadId);
        if (thread is null)
        {
            _setStatus($"Thread '{threadId}' was not found.", false, StatusTone.Warning);
            return;
        }

        _resetPendingThreadTabSelection();
        EnsureThreadTab(thread);
        if (!ViewState.OpenThreadIds.Contains(threadId, StringComparer.OrdinalIgnoreCase))
        {
            ViewState.OpenThreadIds.Add(threadId);
        }

        ViewState.SelectedThreadId = threadId;
        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        _selectionCoordinator.SelectThread(thread);
        if (ThreadHistoryCoordinator.CanLoadThreadHistory(thread) && !_isBackendReady(thread))
        {
            PendingStartupThreadRestoreId = thread.ThreadId;
        }

        _ = PersistViewStateAsync();
        _refreshSelectionAndThreadWorkspace();
        _ = _ensureThreadHistoryLoadedAsync(thread, CancellationToken.None);
    }

    public async Task CloseSelectedThreadAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedThreadId))
        {
            return;
        }

        await CloseThreadTabAsync(SelectedThreadId);
    }

    public async Task CloseThreadTabAsync(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        _resetPendingThreadTabSelection();
        var removedSelectedThread = string.Equals(SelectedThreadId, threadId, StringComparison.OrdinalIgnoreCase);
        var removedThread = FindThread(threadId);
        ViewState.OpenThreadIds.RemoveAll(id => string.Equals(id, threadId, StringComparison.OrdinalIgnoreCase));
        _removeTabPage(threadId);
        if (removedSelectedThread)
        {
            var nextThreadId = ViewState.OpenThreadIds.FirstOrDefault();
            ViewState.SelectedThreadId = nextThreadId;
            _selectionCoordinator.ApplyThreadRemovalFallback(nextThreadId, removedThread?.ProjectRef, Projects, Threads);
        }

        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        await PersistViewStateAsync();
        _refreshSelectionAndThreadWorkspace();
    }

    public async Task RemoveDeletedThreadArtifactsAsync(IReadOnlyList<string> threadIds)
    {
        ArgumentNullException.ThrowIfNull(threadIds);

        var removedAnyState = false;
        foreach (var threadId in threadIds)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                continue;
            }

            _deletePromptDraft(threadId);
            PendingStartupThreadRestoreId = string.Equals(PendingStartupThreadRestoreId, threadId, StringComparison.OrdinalIgnoreCase)
                ? null
                : PendingStartupThreadRestoreId;
            removedAnyState |= ViewState.ThreadStates.Remove(threadId);
        }

        if (!removedAnyState)
        {
            return;
        }

        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        await PersistViewStateAsync();
    }

    public void RemoveDeletedThread(string threadId, string? fallbackProjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        _resetPendingThreadTabSelection();
        var removedSelectedThread = string.Equals(SelectedThreadId, threadId, StringComparison.OrdinalIgnoreCase);
        ViewState.OpenThreadIds.RemoveAll(id => string.Equals(id, threadId, StringComparison.OrdinalIgnoreCase));
        _removeTabPage(threadId);
        _OpenThreadStateStore.RemoveThreadTab(threadId);
        _deletePromptDraft(threadId);

        if (string.Equals(ViewState.SelectedThreadId, threadId, StringComparison.OrdinalIgnoreCase))
        {
            ViewState.SelectedThreadId = null;
        }

        if (removedSelectedThread)
        {
            _selectionCoordinator.ApplyThreadRemovalFallback(nextSelectedThreadId: null, fallbackProjectId, Projects, Threads);
        }

        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        _ = PersistViewStateAsync();
        _refreshSelectionAndThreadWorkspace();
    }

    public void RemoveDeletedProject(string projectId, IReadOnlyList<string> deletedThreadIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(deletedThreadIds);

        _resetPendingThreadTabSelection();
        var removedSelectedThread = deletedThreadIds.Contains(SelectedThreadId, StringComparer.OrdinalIgnoreCase);
        var removedSelectedProject = !GlobalScopeSelected &&
            string.Equals(SelectedProjectId, projectId, StringComparison.OrdinalIgnoreCase);

        foreach (var threadId in deletedThreadIds)
        {
            ViewState.OpenThreadIds.RemoveAll(id => string.Equals(id, threadId, StringComparison.OrdinalIgnoreCase));
            _removeTabPage(threadId);
            _OpenThreadStateStore.RemoveThreadTab(threadId);
            _deletePromptDraft(threadId);

            if (string.Equals(ViewState.SelectedThreadId, threadId, StringComparison.OrdinalIgnoreCase))
            {
                ViewState.SelectedThreadId = null;
            }
        }

        if (removedSelectedThread)
        {
            _selectionCoordinator.ApplyThreadRemovalFallback(nextSelectedThreadId: null, fallbackProjectId: null, Projects, Threads);
        }
        else if (removedSelectedProject && string.IsNullOrWhiteSpace(SelectedThreadId))
        {
            _selectionCoordinator.SelectGlobalScope(Projects);
        }

        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        _ = PersistViewStateAsync();
        _refreshSelectionAndThreadWorkspace();
    }

    public OpenThreadState EnsureThreadTab(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        return _OpenThreadStateStore.EnsureThreadTab(thread);
    }

    public void ResetThreadTab(OpenThreadState tab)
        => _OpenThreadStateStore.ResetThreadTab(tab);

    public OpenThreadState? FindOpenThread(string threadId)
        => _OpenThreadStateStore.FindOpenThread(threadId);

    public ProjectDescriptor? GetSelectedProject()
    {
        var selectedThread = GetSelectedThread();
        return selectedThread?.ProjectRef is { } projectId
            ? GetProjectById(projectId)
            : GetProjectById(SelectedProjectId);
    }

    public ProjectDescriptor? GetProjectById(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        return _catalogStateCoordinator.GetProjectById(projectId);
    }

    public WorkThreadDescriptor? GetSelectedThread()
        => FindThread(SelectedThreadId);

    public WorkThreadDescriptor? FindThread(string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        return _catalogStateCoordinator.FindThread(threadId);
    }

    public async Task RestoreStartupThreadHistoryAsync(string? threadId, CancellationToken cancellationToken)
    {
        var thread = FindThread(threadId);
        if (thread is null)
        {
            return;
        }

        await _ensureThreadHistoryLoadedAsync(thread, cancellationToken);
    }

}
