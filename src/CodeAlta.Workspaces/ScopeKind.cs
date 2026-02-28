namespace CodeAlta.Workspaces;

/// <summary>
/// Represents supported scope kinds for workspace resolution.
/// </summary>
public enum ScopeKind
{
    /// <summary>
    /// All workspaces.
    /// </summary>
    Global = 0,

    /// <summary>
    /// A specific workspace.
    /// </summary>
    Workspace = 1,

    /// <summary>
    /// A specific project.
    /// </summary>
    Project = 2,
}
