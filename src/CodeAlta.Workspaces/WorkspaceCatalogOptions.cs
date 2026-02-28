namespace CodeAlta.Workspaces;

/// <summary>
/// Options used when loading workspace descriptors.
/// </summary>
public sealed class WorkspaceCatalogOptions
{
    /// <summary>
    /// Gets or sets the path to the global repository root.
    /// </summary>
    public string GlobalRepoRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets the workspace root path under the global repository.
    /// </summary>
    public string WorkspacesRoot => Path.Combine(GlobalRepoRoot, "workspaces");
}
