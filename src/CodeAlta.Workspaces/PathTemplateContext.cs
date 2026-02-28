namespace CodeAlta.Workspaces;

/// <summary>
/// Context values used for checkout path template expansion.
/// </summary>
public sealed class PathTemplateContext
{
    /// <summary>
    /// Gets or sets the workspace key.
    /// </summary>
    public string WorkspaceKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the project key.
    /// </summary>
    public string ProjectKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository name.
    /// </summary>
    public string RepoName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the machine id.
    /// </summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public WorkspaceId WorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets the project id.
    /// </summary>
    public ProjectId ProjectId { get; set; }

    /// <summary>
    /// Gets or sets an optional base root used for safe path normalization.
    /// </summary>
    public string? BaseRoot { get; set; }
}
