using CodeAlta.Agent;

namespace CodeAlta.Orchestration;

/// <summary>
/// Base type for runtime events.
/// </summary>
public abstract record OrchestrationEvent(DateTimeOffset Timestamp);

/// <summary>
/// Emitted when the runtime attaches a provider runtime to an active CodeAlta session.
/// </summary>
public sealed record AgentSessionAttachedEvent(
    DateTimeOffset Timestamp,
    AgentSessionHandleId SessionHandleId,
    string SessionId,
    AgentBackendId ProviderId,
    string? ParentSessionId)
    : OrchestrationEvent(Timestamp);

/// <summary>
/// Emitted when the hub submits a send/steer operation and receives a backend run id.
/// </summary>
public sealed record RunStartedEvent(
    DateTimeOffset Timestamp,
    AgentSessionHandleId SessionHandleId,
    AgentRunId RunId)
    : OrchestrationEvent(Timestamp);

/// <summary>
/// Emitted when the hub-level send/steer operation returns successfully; provider-native streaming may continue through session events.
/// </summary>
public sealed record RunCompletedEvent(
    DateTimeOffset Timestamp,
    AgentSessionHandleId SessionHandleId,
    AgentRunId RunId)
    : OrchestrationEvent(Timestamp);

/// <summary>
/// Emitted when the hub-level send/steer operation fails before returning a backend run id.
/// </summary>
public sealed record RunFailedEvent(
    DateTimeOffset Timestamp,
    AgentSessionHandleId SessionHandleId,
    string Message)
    : OrchestrationEvent(Timestamp);
