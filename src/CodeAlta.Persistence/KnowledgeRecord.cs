namespace CodeAlta.Persistence;

/// <summary>
/// Represents a durable knowledge record.
/// </summary>
public sealed record KnowledgeRecord
{
    /// <summary>
    /// Gets or sets the knowledge identifier.
    /// </summary>
    public KnowledgeRecordId KnowledgeRecordId { get; set; } = KnowledgeRecordId.NewVersion7();

    /// <summary>
    /// Gets or sets the stable URI.
    /// </summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional project identifier.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the artifact path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets cached metadata as JSON.
    /// </summary>
    public string? MetadataJson { get; set; }
}
