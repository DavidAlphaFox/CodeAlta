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

    private readonly ProjectCatalog _projectCatalog;
    private readonly WorkThreadCatalog _threadCatalog;
    private readonly ShellSelectionCoordinator _selectionCoordinator = new();
    private readonly OpenThreadRegistry _openThreadRegistry;
    private readonly ThreadViewStateCoordinator _viewStateCoordinator;
    private readonly Func<WorkThreadDescriptor, bool> _isBackendReady;
    private readonly Action<string> _deletePromptDraft;
    private readonly Func<WorkThreadDescriptor, CancellationToken, Task> _ensureThreadHistoryLoadedAsync;
    private readonly Action _refreshSelectionAndThreadWorkspace;
    private readonly Action _refreshCatalogAndThreadWorkspace;
    private readonly Action _resetPendingThreadTabSelection;
    private readonly Action<string> _removeTabPage;
    private readonly Action<string, bool, StatusTone> _setStatus;
    private IReadOnlyList<ProjectDescriptor> _projects = [];
    private IReadOnlyList<WorkThreadDescriptor> _threads = [];

    public ShellThreadStateCoordinator(
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        Func<IUiDispatcher> getUiDispatcher,
        Func<Rectangle?> getTimelineBounds,
        Func<WorkThreadDescriptor, bool> isBackendReady,
        Func<string, string?> loadPromptDraft,
        Action<string> deletePromptDraft,
        Action<OpenThreadState> applyThreadPreference,
        Action<string, string?, AgentReasoningEffort?, bool, bool> rememberThreadPreference,
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

        _projectCatalog = projectCatalog;
        _threadCatalog = threadCatalog;
        _viewStateCoordinator = new ThreadViewStateCoordinator(threadCatalog);
        _openThreadRegistry = new OpenThreadRegistry(
            getUiDispatcher,
            getTimelineBounds,
            loadPromptDraft,
            applyThreadPreference,
            rememberThreadPreference,
            GetSelectedProject);
        _isBackendReady = isBackendReady;
        _deletePromptDraft = deletePromptDraft;
        _ensureThreadHistoryLoadedAsync = ensureThreadHistoryLoadedAsync;
        _refreshSelectionAndThreadWorkspace = refreshSelectionAndThreadWorkspace;
        _refreshCatalogAndThreadWorkspace = refreshCatalogAndThreadWorkspace;
        _resetPendingThreadTabSelection = resetPendingThreadTabSelection;
        _removeTabPage = removeTabPage;
        _setStatus = setStatus;
    }

    public IReadOnlyList<ProjectDescriptor> Projects => _projects;

    public IReadOnlyList<WorkThreadDescriptor> Threads => _threads;

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
        var projects = await _projectCatalog.LoadAsync(cancellationToken).ConfigureAwait(false);
        var threads = await _threadCatalog.LoadInternalAsync(cancellationToken).ConfigureAwait(false);
        var viewState = await _viewStateCoordinator.LoadViewStateAsync(cancellationToken).ConfigureAwait(false);
        _viewStateCoordinator.ApplyThreadLocalState(threads, viewState);
        return new InitialCatalogState(projects, threads, viewState);
    }

    public void ApplyInitialCatalogState(InitialCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _projects = state.Projects;
        _threads = state.Threads;
        _selectionCoordinator.ApplyInitialSelection(state.ViewState, _projects, _threads);
    }

    public async Task LoadCatalogStateAsync(CancellationToken cancellationToken)
    {
        ApplyInitialCatalogState(await LoadInitialCatalogStateAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task PersistViewStateAsync()
        => await _viewStateCoordinator.PersistViewStateAsync(ViewState).ConfigureAwait(false);

    public void ApplyRecoveredCatalogState(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(threads);

        _projects = projects;
        _threads = _viewStateCoordinator.ApplyThreadLocalState(threads, ViewState);
        _openThreadRegistry.PruneRetainedThreadState(_threads);

        ViewState.OpenThreadIds.RemoveAll(id => _threads.All(thread => !string.Equals(thread.ThreadId, id, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(ViewState.SelectedThreadId) &&
            ViewState.OpenThreadIds.All(id => !string.Equals(id, ViewState.SelectedThreadId, StringComparison.OrdinalIgnoreCase)))
        {
            ViewState.SelectedThreadId = null;
        }

        if (string.IsNullOrWhiteSpace(SelectedThreadId) &&
            !string.IsNullOrWhiteSpace(PendingStartupThreadRestoreId) &&
            FindThread(PendingStartupThreadRestoreId) is { } restoredThread)
        {
            if (!ViewState.OpenThreadIds.Contains(restoredThread.ThreadId, StringComparer.OrdinalIgnoreCase))
            {
                ViewState.OpenThreadIds.Insert(0, restoredThread.ThreadId);
            }

            ViewState.SelectedThreadId = restoredThread.ThreadId;
            _selectionCoordinator.SelectThread(restoredThread);
        }

        EnsureSelectionDefaults();
        _refreshCatalogAndThreadWorkspace();
    }

    public async Task PersistThreadLocalStateAsync(WorkThreadDescriptor thread)
        => await _viewStateCoordinator.PersistThreadLocalStateAsync(ViewState, thread).ConfigureAwait(false);

    public NavigatorSettings GetNavigatorSettingsSnapshot()
        => _viewStateCoordinator.GetNavigatorSettingsSnapshot(ViewState);

    public async Task SaveNavigatorSettingsAsync(NavigatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        await _viewStateCoordinator.SaveNavigatorSettingsAsync(ViewState, settings).ConfigureAwait(false);
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
        _selectionCoordinator.SelectGlobalScope(_projects);
        ViewState.SelectedThreadId = null;
        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        _ = PersistViewStateAsync();
        _refreshSelectionAndThreadWorkspace();
    }

    public void SelectProjectScope(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        _resetPendingThreadTabSelection();
        _selectionCoordinator.SelectProjectScope(projectId, _projects);
        ViewState.SelectedThreadId = null;
        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        _ = PersistViewStateAsync();
        _refreshSelectionAndThreadWorkspace();
    }

    public void EnsureSelectionDefaults()
        => _selectionCoordinator.EnsureSelectionDefaults(_projects, _threads);

    public async Task RegisterCreatedThreadAsync(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        var threads = _threads.ToList();
        threads.RemoveAll(existing => string.Equals(existing.ThreadId, thread.ThreadId, StringComparison.OrdinalIgnoreCase));
        threads.Add(thread);
        _threads = threads
            .OrderByDescending(static item => item.LastActiveAt)
            .ToArray();

        OpenThread(thread.ThreadId);
        await _ensureThreadHistoryLoadedAsync(thread, CancellationToken.None).ConfigureAwait(false);
    }

    public OpenThreadState RegisterDelegatedThread(WorkThreadDescriptor child, OpenThreadState sourceTab)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(sourceTab);

        _threads = _threads
            .Where(existing => !string.Equals(existing.ThreadId, child.ThreadId, StringComparison.OrdinalIgnoreCase))
            .Append(child)
            .OrderByDescending(static item => item.LastActiveAt)
            .ToArray();

        if (!ViewState.OpenThreadIds.Contains(child.ThreadId, StringComparer.OrdinalIgnoreCase))
        {
            ViewState.OpenThreadIds.Add(child.ThreadId);
            ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var childTab = EnsureThreadTab(child);
        childTab.BackendId = sourceTab.BackendId;
        childTab.ModelId = sourceTab.ModelId;
        childTab.ReasoningEffort = sourceTab.ReasoningEffort;
        childTab.AutoScroll = sourceTab.AutoScroll;
        return childTab;
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

        await CloseThreadAsync(SelectedThreadId).ConfigureAwait(false);
    }

    public async Task CloseThreadAsync(string threadId)
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
            _selectionCoordinator.ApplyThreadRemovalFallback(nextThreadId, removedThread?.ProjectRef, _projects, _threads);
        }

        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        await PersistViewStateAsync().ConfigureAwait(false);
        _refreshSelectionAndThreadWorkspace();
    }

    public void RemoveDeletedThread(string threadId, string? fallbackProjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        _resetPendingThreadTabSelection();
        var removedSelectedThread = string.Equals(SelectedThreadId, threadId, StringComparison.OrdinalIgnoreCase);
        ViewState.OpenThreadIds.RemoveAll(id => string.Equals(id, threadId, StringComparison.OrdinalIgnoreCase));
        _removeTabPage(threadId);
        _openThreadRegistry.RemoveThreadTab(threadId);
        _deletePromptDraft(threadId);

        if (string.Equals(ViewState.SelectedThreadId, threadId, StringComparison.OrdinalIgnoreCase))
        {
            ViewState.SelectedThreadId = null;
        }

        if (removedSelectedThread)
        {
            _selectionCoordinator.ApplyThreadRemovalFallback(nextSelectedThreadId: null, fallbackProjectId, _projects, _threads);
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
            _openThreadRegistry.RemoveThreadTab(threadId);
            _deletePromptDraft(threadId);

            if (string.Equals(ViewState.SelectedThreadId, threadId, StringComparison.OrdinalIgnoreCase))
            {
                ViewState.SelectedThreadId = null;
            }
        }

        if (removedSelectedThread)
        {
            _selectionCoordinator.ApplyThreadRemovalFallback(nextSelectedThreadId: null, fallbackProjectId: null, _projects, _threads);
        }
        else if (removedSelectedProject && string.IsNullOrWhiteSpace(SelectedThreadId))
        {
            _selectionCoordinator.SelectGlobalScope(_projects);
        }

        ViewState.UpdatedAt = DateTimeOffset.UtcNow;
        _ = PersistViewStateAsync();
        _refreshSelectionAndThreadWorkspace();
    }

    public OpenThreadState EnsureThreadTab(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        return _openThreadRegistry.EnsureThreadTab(thread);
    }

    public void ResetThreadTab(OpenThreadState tab)
        => _openThreadRegistry.ResetThreadTab(tab);

    public OpenThreadState? FindOpenThread(string threadId)
        => _openThreadRegistry.FindOpenThread(threadId);

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

        return _projects.FirstOrDefault(project => string.Equals(project.Id, projectId, StringComparison.OrdinalIgnoreCase));
    }

    public WorkThreadDescriptor? GetSelectedThread()
        => FindThread(SelectedThreadId);

    public WorkThreadDescriptor? FindThread(string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        return _threads.FirstOrDefault(thread => string.Equals(thread.ThreadId, threadId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RestoreStartupThreadHistoryAsync(string? threadId, CancellationToken cancellationToken)
    {
        var thread = FindThread(threadId);
        if (thread is null)
        {
            return;
        }

        await _ensureThreadHistoryLoadedAsync(thread, cancellationToken).ConfigureAwait(false);
    }

}
