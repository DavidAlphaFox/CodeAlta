using CodeAlta.Persistence;

namespace CodeAlta.Orchestration;

/// <summary>
/// Represents a request to create a durable plan.
/// </summary>
public sealed record PlannerPlanRequest
{
    /// <summary>
    /// Gets or sets goal title for the root task.
    /// </summary>
    public required string Goal { get; init; }

    /// <summary>
    /// Gets or sets optional workspace id.
    /// </summary>
    public string? WorkspaceId { get; init; }

    /// <summary>
    /// Gets or sets optional project id.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// Gets or sets child step titles.
    /// </summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    /// <summary>
    /// Gets or sets optional assigned planner agent id.
    /// </summary>
    public string? AssignedAgentId { get; init; }
}

/// <summary>
/// Represents the result of creating a durable plan.
/// </summary>
public sealed record PlannerPlanResult
{
    /// <summary>
    /// Gets or sets the root task id.
    /// </summary>
    public required TaskId RootTaskId { get; init; }

    /// <summary>
    /// Gets or sets created child task ids in plan order.
    /// </summary>
    public required IReadOnlyList<TaskId> ChildTaskIds { get; init; }

    /// <summary>
    /// Gets or sets persisted plan artifact id.
    /// </summary>
    public required ArtifactId PlanArtifactId { get; init; }
}
