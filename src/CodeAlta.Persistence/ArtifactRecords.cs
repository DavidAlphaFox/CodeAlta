namespace CodeAlta.Persistence;

/// <summary>
/// Represents a persisted artifact metadata record.
/// </summary>
public sealed record ArtifactRecord
{
    /// <summary>
    /// Gets the artifact identifier.
    /// </summary>
    public required ArtifactId ArtifactId { get; init; }

    /// <summary>
    /// Gets the stable artifact URI.
    /// </summary>
    public required string Uri { get; init; }

    /// <summary>
    /// Gets the optional project identifier.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// Gets the artifact type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the canonical path to the artifact.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets cached frontmatter metadata as JSON text.
    /// </summary>
    public string? FrontmatterJson { get; init; }

    /// <summary>
    /// Gets the creation timestamp in UTC.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the last update timestamp in UTC.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Represents an artifact link row.
/// </summary>
public sealed record ArtifactLinkRecord
{
    /// <summary>
    /// Gets the source artifact identifier.
    /// </summary>
    public required ArtifactId FromArtifactId { get; init; }

    /// <summary>
    /// Gets the linked entity kind.
    /// </summary>
    public required string ToKind { get; init; }

    /// <summary>
    /// Gets the linked entity identifier.
    /// </summary>
    public required string ToId { get; init; }
}

/// <summary>
/// Query arguments for artifact listing.
/// </summary>
public sealed record ArtifactQuery
{
    /// <summary>
    /// Gets or sets an optional project identifier filter.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets an optional artifact type filter.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the result limit.
    /// </summary>
    public int Limit { get; set; } = 100;
}
