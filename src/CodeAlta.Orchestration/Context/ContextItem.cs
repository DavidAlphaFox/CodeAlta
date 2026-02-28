namespace CodeAlta.Orchestration.Context;

/// <summary>
/// Represents an item included in an orchestration context pack.
/// </summary>
public sealed record ContextItem
{
    /// <summary>
    /// Gets or sets an item title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets or sets item content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets or sets the stable source URI for this item.
    /// </summary>
    public required string SourceUri { get; init; }

    /// <summary>
    /// Gets or sets item priority. Lower values are higher priority.
    /// </summary>
    public int Priority { get; init; }
}
