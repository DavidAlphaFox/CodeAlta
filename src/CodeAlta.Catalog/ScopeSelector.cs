namespace CodeAlta.Catalog;

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
    /// Gets the optional project slug.
    /// </summary>
    public string? ProjectSlug { get; init; }

    /// <summary>
    /// Creates a selector for the global scope.
    /// </summary>
    /// <returns>A selector targeting all projects.</returns>
    public static ScopeSelector Global() => new() { Kind = ScopeKind.Global };

    /// <summary>
    /// Creates a selector for a project slug.
    /// </summary>
    /// <param name="projectSlug">The project slug.</param>
    /// <returns>A project selector.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectSlug"/> is invalid.</exception>
    public static ScopeSelector Project(string projectSlug)
    {
        CatalogSlugValidator.Validate(projectSlug, nameof(projectSlug));
        return new ScopeSelector { Kind = ScopeKind.Project, ProjectSlug = projectSlug };
    }
}

