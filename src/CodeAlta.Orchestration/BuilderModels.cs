using CodeAlta.Persistence;

namespace CodeAlta.Orchestration;

/// <summary>
/// Represents a builder completion request for a task.
/// </summary>
public sealed record BuilderExecutionRequest
{
    /// <summary>
    /// Gets or sets the target task id.
    /// </summary>
    public required TaskId TaskId { get; init; }

    /// <summary>
    /// Gets or sets verification summary content.
    /// </summary>
    public required string VerificationSummary { get; init; }

    /// <summary>
    /// Gets or sets optional project id for artifact scoping.
    /// </summary>
    public string? ProjectId { get; init; }
}

/// <summary>
/// Represents a builder completion result.
/// </summary>
public sealed record BuilderExecutionResult
{
    /// <summary>
    /// Gets or sets the completed task id.
    /// </summary>
    public required TaskId TaskId { get; init; }

    /// <summary>
    /// Gets or sets emitted verification artifact id.
    /// </summary>
    public required ArtifactId VerificationArtifactId { get; init; }
}
