namespace CodeAlta.Orchestration;

/// <summary>
/// Represents orchestration scope kinds.
/// </summary>
public enum AgentScopeKind
{
    /// <summary>
    /// Global scope spanning all workspaces.
    /// </summary>
    Global = 0,

    /// <summary>
    /// Workspace-level scope.
    /// </summary>
    Workspace = 1,

    /// <summary>
    /// Project-level scope.
    /// </summary>
    Project = 2,
}
