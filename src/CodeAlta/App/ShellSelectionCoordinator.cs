using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration;

namespace CodeAlta.App;

internal sealed class ShellSelectionCoordinator
{
    private readonly ShellSelectionState _state = new();

    public WorkThreadViewState ViewState
    {
        get => _state.ViewState;
        set => _state.ViewState = value;
    }

    public ShellSelection Selection
    {
        get => _state.Selection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _state.Selection = value;
            ViewState.Selection = ToPersistedSelection(value);
            ViewState.SelectedThreadId = value.SelectedThreadId;
        }
    }

    public string? PendingStartupThreadRestoreId
    {
        get => _state.PendingStartupThreadRestoreId;
        set => _state.PendingStartupThreadRestoreId = value;
    }

    public bool DraftTabOpen
    {
        get => Selection.DraftTabOpen;
        set
        {
            if (!value || Selection.DraftTabOpen)
            {
                return;
            }

            Selection = Selection.GlobalScopeSelected
                ? ShellSelection.GlobalDraft(Selection.SelectedProjectId)
                : Selection.SelectedProjectId is { } projectId
                    ? ShellSelection.ProjectDraft(projectId)
                    : ShellSelection.GlobalDraft();
        }
    }

    public bool GlobalScopeSelected
    {
        get => Selection.GlobalScopeSelected;
        set
        {
            if (!Selection.DraftTabOpen)
            {
                return;
            }

            Selection = new ShellSelection(
                ShellSurface.DraftWorkspace,
                new WorkspaceTarget.Draft(Selection.SelectedProjectId, value));
        }
    }

    public string? SelectedProjectId
    {
        get => Selection.SelectedProjectId;
        set
        {
            Selection = Selection switch
            {
                { Target: WorkspaceTarget.Draft draft } => new ShellSelection(
                    ShellSurface.DraftWorkspace,
                    new WorkspaceTarget.Draft(value, draft.IsGlobal)),
                { Target: WorkspaceTarget.Thread thread } => thread.ThreadId is { Length: > 0 } threadId
                    ? ShellSelection.Thread(threadId, value)
                    : Selection,
                _ => Selection,
            };
        }
    }

    public string? SelectedThreadId
    {
        get => Selection.SelectedThreadId;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Selection = Selection.GlobalScopeSelected
                    ? ShellSelection.GlobalDraft(Selection.SelectedProjectId)
                    : Selection.SelectedProjectId is { } projectId
                        ? ShellSelection.ProjectDraft(projectId)
                        : ShellSelection.GlobalDraft();
                return;
            }

            Selection = ShellSelection.Thread(value, Selection.SelectedProjectId);
        }
    }

    public void ApplyInitialSelection(
        WorkThreadViewState viewState,
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(threads);

        ViewState = viewState;
        var persistedSelection = NormalizePersistedSelection(ViewState.Selection, ViewState.SelectedThreadId, ViewState.OpenThreadIds);
        PendingStartupThreadRestoreId = persistedSelection.Surface == WorkThreadSelectionSurface.Thread
            ? persistedSelection.ThreadId
            : null;

        if (persistedSelection.Surface == WorkThreadSelectionSurface.Thread &&
            persistedSelection.ThreadId is { Length: > 0 } desiredThreadId &&
            FindThread(threads, desiredThreadId) is { } thread)
        {
            Selection = ShellSelection.Thread(thread.ThreadId, thread.ProjectRef);
            return;
        }

        var preferredProjectId = NormalizeProjectId(projects, persistedSelection.ProjectId ?? projects.FirstOrDefault()?.Id);
        Selection = persistedSelection.DraftScope == WorkThreadDraftScope.Global
            ? ShellSelection.GlobalDraft(preferredProjectId)
            : preferredProjectId is { } projectId
                ? ShellSelection.ProjectDraft(projectId)
                : ShellSelection.GlobalDraft();
    }

    public void EnsureSelectionDefaults(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(threads);

        if (Selection.SelectedThreadId is { } selectedThreadId &&
            FindThread(threads, selectedThreadId) is not { } thread)
        {
            Selection = BuildDraftFallback(projects, Selection.SelectedProjectId);
            return;
        }

        if (Selection.Target is WorkspaceTarget.Thread currentThread &&
            FindThread(threads, currentThread.ThreadId) is { } selectedThread)
        {
            Selection = ShellSelection.Thread(selectedThread.ThreadId, NormalizeProjectId(projects, selectedThread.ProjectRef));
            return;
        }

        var normalizedProjectId = NormalizeProjectId(projects, Selection.SelectedProjectId);
        Selection = Selection.GlobalScopeSelected
            ? ShellSelection.GlobalDraft(normalizedProjectId)
            : normalizedProjectId is { } projectId
                ? ShellSelection.ProjectDraft(projectId)
                : ShellSelection.GlobalDraft();
    }

    public void SelectGlobalScope(IReadOnlyList<ProjectDescriptor> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        Selection = ShellSelection.GlobalDraft(NormalizeProjectId(projects, Selection.SelectedProjectId));
    }

    public void SelectProjectScope(string projectId, IReadOnlyList<ProjectDescriptor> projects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(projects);
        Selection = NormalizeProjectId(projects, projectId) is { } normalizedProjectId
            ? ShellSelection.ProjectDraft(normalizedProjectId)
            : ShellSelection.GlobalDraft();
    }

    public void SelectThread(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        Selection = ShellSelection.Thread(thread.ThreadId, thread.ProjectRef);
    }

    public void ApplyThreadRemovalFallback(
        string? nextSelectedThreadId,
        string? fallbackProjectId,
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(threads);

        if (!string.IsNullOrWhiteSpace(nextSelectedThreadId) &&
            FindThread(threads, nextSelectedThreadId) is { } nextThread)
        {
            Selection = ShellSelection.Thread(nextThread.ThreadId, nextThread.ProjectRef);
            return;
        }

        Selection = BuildDraftFallback(projects, fallbackProjectId ?? Selection.SelectedProjectId);
    }

    private static ShellSelection BuildDraftFallback(
        IReadOnlyList<ProjectDescriptor> projects,
        string? preferredProjectId)
    {
        var projectId = NormalizeProjectId(projects, preferredProjectId);
        return preferredProjectId is { Length: > 0 } && projectId is not null
            ? ShellSelection.ProjectDraft(projectId)
            : ShellSelection.GlobalDraft(projectId);
    }

    private static string? NormalizeProjectId(IReadOnlyList<ProjectDescriptor> projects, string? projectId)
    {
        ArgumentNullException.ThrowIfNull(projects);

        if (string.IsNullOrWhiteSpace(projectId))
        {
            return projects.FirstOrDefault()?.Id;
        }

        return projects.FirstOrDefault(project =>
            string.Equals(project.Id, projectId, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static WorkThreadDescriptor? FindThread(IReadOnlyList<WorkThreadDescriptor> threads, string threadId)
    {
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        return threads.FirstOrDefault(thread => string.Equals(thread.ThreadId, threadId, StringComparison.OrdinalIgnoreCase));
    }

    private static WorkThreadSelectionState NormalizePersistedSelection(
        WorkThreadSelectionState? selection,
        string? legacySelectedThreadId,
        IReadOnlyList<string> openThreadIds)
    {
        if (selection is not null)
        {
            return selection;
        }

        if (!string.IsNullOrWhiteSpace(legacySelectedThreadId))
        {
            return WorkThreadSelectionState.Thread(legacySelectedThreadId, projectId: null);
        }

        if (openThreadIds.FirstOrDefault(static threadId => !string.IsNullOrWhiteSpace(threadId)) is { } firstOpenThreadId)
        {
            return WorkThreadSelectionState.Thread(firstOpenThreadId, projectId: null);
        }

        return WorkThreadSelectionState.GlobalDraft();
    }

    private static WorkThreadSelectionState ToPersistedSelection(ShellSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return selection.Target switch
        {
            WorkspaceTarget.Draft { IsGlobal: true } draft => WorkThreadSelectionState.GlobalDraft(draft.ProjectId),
            WorkspaceTarget.Draft draft => draft.ProjectId is { Length: > 0 } projectId
                ? WorkThreadSelectionState.ProjectDraft(projectId)
                : WorkThreadSelectionState.GlobalDraft(),
            WorkspaceTarget.Thread thread => WorkThreadSelectionState.Thread(thread.ThreadId, thread.ProjectId),
            _ => WorkThreadSelectionState.GlobalDraft(selection.SelectedProjectId),
        };
    }
}
