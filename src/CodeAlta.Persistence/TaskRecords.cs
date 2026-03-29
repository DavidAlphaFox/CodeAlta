namespace CodeAlta.Persistence;

/// <summary>
/// Represents a persisted task.
/// </summary>
public sealed record TaskRecord
{
    /// <summary>
    /// Gets the task identifier.
    /// </summary>
    public required TaskId TaskId { get; init; }

    /// <summary>
    /// Gets the optional project identifier.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// Gets the optional parent task identifier.
    /// </summary>
    public string? ParentTaskId { get; init; }

    /// <summary>
    /// Gets the task title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the task status.
    /// </summary>
    public required TaskStatus Status { get; init; }

    /// <summary>
    /// Gets the assigned agent identifier.
    /// </summary>
    public string? AssignedAgentId { get; init; }

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
/// Represents a persisted task event.
/// </summary>
public sealed record TaskEventRecord
{
    /// <summary>
    /// Gets the auto-generated event identifier.
    /// </summary>
    public required long EventId { get; init; }

    /// <summary>
    /// Gets the task identifier.
    /// </summary>
    public required TaskId TaskId { get; init; }

    /// <summary>
    /// Gets the event kind.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets optional event payload as JSON text.
    /// </summary>
    public string? PayloadJson { get; init; }

    /// <summary>
    /// Gets the creation timestamp in UTC.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Request data used when creating a task.
/// </summary>
public sealed record CreateTaskRequest
{
    /// <summary>
    /// Gets or sets the optional explicit task identifier.
    /// </summary>
    public TaskId? TaskId { get; set; }

    /// <summary>
    /// Gets or sets the optional project identifier.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the optional parent task identifier.
    /// </summary>
    public string? ParentTaskId { get; set; }

    /// <summary>
    /// Gets or sets the task title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the initial task status.
    /// </summary>
    public TaskStatus Status { get; set; } = TaskStatus.Pending;

    /// <summary>
    /// Gets or sets the assigned agent identifier.
    /// </summary>
    public string? AssignedAgentId { get; set; }
}

/// <summary>
/// Request data used when updating a task.
/// </summary>
public sealed record UpdateTaskRequest
{
    /// <summary>
    /// Gets or sets the target task identifier.
    /// </summary>
    public required TaskId TaskId { get; set; }

    /// <summary>
    /// Gets or sets the replacement title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the replacement status.
    /// </summary>
    public TaskStatus? Status { get; set; }

    /// <summary>
    /// Gets or sets the replacement assigned agent identifier.
    /// </summary>
    public string? AssignedAgentId { get; set; }
}
