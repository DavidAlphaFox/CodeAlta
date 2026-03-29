namespace CodeAlta.Catalog;

/// <summary>
/// Represents supported scope kinds for project resolution.
/// </summary>
public enum ScopeKind
{
    /// <summary>
    /// The global scope spanning all projects.
    /// </summary>
    Global = 0,

    /// <summary>
    /// A specific project.
    /// </summary>
    Project = 1,
}

