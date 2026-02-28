using CodeAlta.Agent;
using CodeAlta.Persistence;

namespace CodeAlta.Orchestration;

/// <summary>
/// Base type for orchestration events.
/// </summary>
public abstract record OrchestrationEvent(DateTimeOffset Timestamp);

/// <summary>
/// Emitted when a run starts.
/// </summary>
/// <param name="Timestamp">Event timestamp in UTC.</param>
/// <param name="AgentId">Agent id.</param>
/// <param name="RunId">Backend run id.</param>
public sealed record RunStartedEvent(
    DateTimeOffset Timestamp,
    AgentId AgentId,
    AgentRunId RunId)
    : OrchestrationEvent(Timestamp);

/// <summary>
/// Emitted when a run completes successfully.
/// </summary>
/// <param name="Timestamp">Event timestamp in UTC.</param>
/// <param name="AgentId">Agent id.</param>
/// <param name="RunId">Backend run id.</param>
public sealed record RunCompletedEvent(
    DateTimeOffset Timestamp,
    AgentId AgentId,
    AgentRunId RunId)
    : OrchestrationEvent(Timestamp);

/// <summary>
/// Emitted when a run fails.
/// </summary>
/// <param name="Timestamp">Event timestamp in UTC.</param>
/// <param name="AgentId">Agent id.</param>
/// <param name="Message">Failure message.</param>
public sealed record RunFailedEvent(
    DateTimeOffset Timestamp,
    AgentId AgentId,
    string Message)
    : OrchestrationEvent(Timestamp);

/// <summary>
/// Emitted when a task changes.
/// </summary>
/// <param name="Timestamp">Event timestamp in UTC.</param>
/// <param name="TaskId">Task id.</param>
/// <param name="Status">Task status string.</param>
public sealed record TaskUpdatedEvent(
    DateTimeOffset Timestamp,
    TaskId TaskId,
    string Status)
    : OrchestrationEvent(Timestamp);
