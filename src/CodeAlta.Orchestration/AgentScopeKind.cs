namespace CodeAlta.Orchestration;

/// <summary>
/// Represents orchestration scope kinds.
/// </summary>
public enum AgentScopeKind
{
    /// <summary>
    /// Global scope spanning all projects.
    /// </summary>
    Global = 0,

    /// <summary>
    /// Project-level scope.
    /// </summary>
    Project = 1,
}
