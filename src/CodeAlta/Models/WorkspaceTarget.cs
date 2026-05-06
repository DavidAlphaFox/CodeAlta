namespace CodeAlta.Models;

internal abstract record WorkspaceTarget
{
    public sealed record Draft(string? ProjectId, bool IsGlobal) : WorkspaceTarget;

    public sealed record Thread(string ThreadId, string? ProjectId) : WorkspaceTarget;
}
