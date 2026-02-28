using CodeAlta.Persistence;

namespace CodeAlta.Orchestration.Context;

/// <summary>
/// Represents request data passed to a context provider.
/// </summary>
public sealed record ContextProviderRequest
{
    /// <summary>
    /// Gets the active agent scope.
    /// </summary>
    public required AgentScope Scope { get; init; }

    /// <summary>
    /// Gets the user query or run goal.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets an optional active task id.
    /// </summary>
    public TaskId? TaskId { get; init; }

    /// <summary>
    /// Gets current remaining character budget.
    /// </summary>
    public int RemainingBudget { get; init; }
}
