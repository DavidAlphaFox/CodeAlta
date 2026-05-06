using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.App.Context;

internal sealed class ThreadSelectionContext
{
    private readonly ShellThreadStateCoordinator _threadStateCoordinator;
    private readonly Func<WorkThreadDescriptor, CancellationToken, Task> _ensureThreadHistoryLoadedAsync;
    private readonly Func<string, bool> _isSelectedThread;

    public ThreadSelectionContext(
        ShellThreadStateCoordinator threadStateCoordinator,
        Func<WorkThreadDescriptor, CancellationToken, Task> ensureThreadHistoryLoadedAsync,
        Func<string, bool> isSelectedThread)
    {
        ArgumentNullException.ThrowIfNull(threadStateCoordinator);
        ArgumentNullException.ThrowIfNull(ensureThreadHistoryLoadedAsync);
        ArgumentNullException.ThrowIfNull(isSelectedThread);

        _threadStateCoordinator = threadStateCoordinator;
        _ensureThreadHistoryLoadedAsync = ensureThreadHistoryLoadedAsync;
        _isSelectedThread = isSelectedThread;
    }

    public IReadOnlyList<ProjectDescriptor> Projects => _threadStateCoordinator.Projects;

    public IReadOnlyList<WorkThreadDescriptor> Threads => _threadStateCoordinator.Threads;

    public IReadOnlyList<string> OpenThreadIds => _threadStateCoordinator.ViewState.OpenThreadIds;

    public ShellSelection Selection => _threadStateCoordinator.Selection;

    public WorkspaceTarget Target => Selection.Target;

    public OpenThreadState EnsureThreadTab(WorkThreadDescriptor thread)
        => _threadStateCoordinator.EnsureThreadTab(thread);

    public OpenThreadState? FindOpenThread(string threadId)
        => _threadStateCoordinator.FindOpenThread(threadId);

    public ProjectDescriptor? GetSelectedProject()
        => _threadStateCoordinator.GetSelectedProject();

    public ProjectDescriptor? GetProjectById(string? projectId)
        => _threadStateCoordinator.GetProjectById(projectId);

    public WorkThreadDescriptor? GetSelectedThread()
        => _threadStateCoordinator.GetSelectedThread();

    public WorkThreadDescriptor? FindThread(string? threadId)
        => _threadStateCoordinator.FindThread(threadId);

    public Task EnsureThreadHistoryLoadedAsync(
        WorkThreadDescriptor thread,
        CancellationToken cancellationToken = default)
        => _ensureThreadHistoryLoadedAsync(thread, cancellationToken);

    public bool IsGlobalDraftSelected()
        => Target is WorkspaceTarget.Draft { IsGlobal: true };

    public bool IsDraftSelected()
        => Target is WorkspaceTarget.Draft;

    public bool HasOpenDraftTab()
        => _threadStateCoordinator.DraftTabOpen;

    public string? GetSelectedProjectId()
        => Selection.SelectedProjectId;

    public string? GetSelectedThreadId()
        => Selection.SelectedThreadId;

    public bool IsSelectedThread(string threadId)
        => _isSelectedThread(threadId);
}
