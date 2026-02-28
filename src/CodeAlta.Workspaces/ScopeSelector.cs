namespace CodeAlta.Workspaces;

/// <summary>
/// Represents a user-facing scope selector.
/// </summary>
public sealed record ScopeSelector
{
    /// <summary>
    /// Gets the scope kind.
    /// </summary>
    public ScopeKind Kind { get; init; }

    /// <summary>
    /// Gets the optional workspace key.
    /// </summary>
    public string? WorkspaceKey { get; init; }

    /// <summary>
    /// Gets the optional project key.
    /// </summary>
    public string? ProjectKey { get; init; }

    /// <summary>
    /// Creates a selector for the global scope.
    /// </summary>
    /// <returns>A selector targeting all workspaces.</returns>
    public static ScopeSelector Global() => new() { Kind = ScopeKind.Global };

    /// <summary>
    /// Creates a selector for a workspace key.
    /// </summary>
    /// <param name="workspaceKey">The workspace key.</param>
    /// <returns>A workspace selector.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="workspaceKey"/> is invalid.</exception>
    public static ScopeSelector Workspace(string workspaceKey)
    {
        WorkspaceKeyValidator.Validate(workspaceKey, nameof(workspaceKey));
        return new ScopeSelector { Kind = ScopeKind.Workspace, WorkspaceKey = workspaceKey };
    }

    /// <summary>
    /// Creates a selector for a project key.
    /// </summary>
    /// <param name="projectKey">The project key.</param>
    /// <returns>A project selector.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectKey"/> is invalid.</exception>
    public static ScopeSelector Project(string projectKey)
    {
        WorkspaceKeyValidator.Validate(projectKey, nameof(projectKey));
        return new ScopeSelector { Kind = ScopeKind.Project, ProjectKey = projectKey };
    }
}
