namespace CodeAlta.Catalog;

/// <summary>
/// Identifies the durable session type.
/// </summary>
public enum SessionViewKind
{
    /// <summary>
    /// A user-facing global session rooted at <c>~/.alta/</c>.
    /// </summary>
    GlobalSession,

    /// <summary>
    /// A user-facing session scoped to exactly one project.
    /// </summary>
    ProjectSession,

    /// <summary>
    /// A legacy host-owned internal session record.
    /// </summary>
    InternalSession,
}
