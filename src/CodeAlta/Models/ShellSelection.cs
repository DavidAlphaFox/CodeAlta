namespace CodeAlta.Models;

internal sealed record ShellSelection(ShellSurface Surface, WorkspaceTarget Target)
{
    public static ShellSelection GlobalDraft(string? projectId = null)
        => new(ShellSurface.DraftWorkspace, new WorkspaceTarget.Draft(projectId, IsGlobal: true));

    public static ShellSelection ProjectDraft(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        return new ShellSelection(ShellSurface.DraftWorkspace, new WorkspaceTarget.Draft(projectId, IsGlobal: false));
    }

    public static ShellSelection Thread(string threadId, string? projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return new ShellSelection(ShellSurface.ThreadWorkspace, new WorkspaceTarget.Thread(threadId, projectId));
    }

    public bool DraftTabOpen => Surface == ShellSurface.DraftWorkspace;

    public bool GlobalScopeSelected => Target is WorkspaceTarget.Draft { IsGlobal: true };

    public string? SelectedProjectId => Target switch
    {
        WorkspaceTarget.Draft draft => draft.ProjectId,
        WorkspaceTarget.Thread thread => thread.ProjectId,
        WorkspaceTarget.Agent agent => agent.ProjectId,
        _ => null,
    };

    public string? SelectedThreadId => Target is WorkspaceTarget.Thread thread ? thread.ThreadId : null;
}
