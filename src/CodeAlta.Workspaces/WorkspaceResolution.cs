namespace CodeAlta.Workspaces;

/// <summary>
/// Represents a fully-resolved workspace scope.
/// </summary>
public sealed record WorkspaceResolution
{
    /// <summary>
    /// Gets the resolved workspace.
    /// </summary>
    public required WorkspaceDescriptor Workspace { get; init; }

    /// <summary>
    /// Gets the resolved projects.
    /// </summary>
    public required IReadOnlyList<ResolvedProject> Projects { get; init; }

    /// <summary>
    /// Gets all relevant <c>.codealta</c> roots for this scope.
    /// </summary>
    public required IReadOnlyList<string> CodeAltaRoots { get; init; }
}
