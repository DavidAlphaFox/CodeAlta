using CodeAlta.Agent;

namespace CodeAlta.Orchestration;

/// <summary>
/// Base type for runtime events.
/// </summary>
public abstract record OrchestrationEvent(DateTimeOffset Timestamp);

/// <summary>
/// Emitted when the hub submits a send/steer operation and receives a backend run id.
/// </summary>
public sealed record RunStartedEvent(
    DateTimeOffset Timestamp,
    AgentId AgentId,
    AgentRunId RunId)
    : OrchestrationEvent(Timestamp);

/// <summary>
/// Emitted when the hub-level send/steer operation returns successfully; provider-native streaming may continue through session events.
/// </summary>
public sealed record RunCompletedEvent(
    DateTimeOffset Timestamp,
    AgentId AgentId,
    AgentRunId RunId)
    : OrchestrationEvent(Timestamp);

/// <summary>
/// Emitted when the hub-level send/steer operation fails before returning a backend run id.
/// </summary>
public sealed record RunFailedEvent(
    DateTimeOffset Timestamp,
    AgentId AgentId,
    string Message)
    : OrchestrationEvent(Timestamp);
