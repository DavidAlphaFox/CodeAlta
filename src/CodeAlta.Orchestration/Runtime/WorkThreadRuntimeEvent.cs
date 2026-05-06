using CodeAlta.Agent;

namespace CodeAlta.Orchestration.Runtime;

/// <summary>
/// Base type for thread runtime events.
/// </summary>
/// <param name="ThreadId">The owning thread id.</param>
/// <param name="Timestamp">The event timestamp.</param>
public abstract record WorkThreadRuntimeEvent(string ThreadId, DateTimeOffset Timestamp);

/// <summary>
/// Wraps a sanitized agent event for a specific thread.
/// </summary>
/// <param name="ThreadId">The owning thread id.</param>
/// <param name="Event">The sanitized agent event.</param>
public sealed record WorkThreadAgentEvent(
    string ThreadId,
    AgentEvent Event)
    : WorkThreadRuntimeEvent(ThreadId, Event.Timestamp);

/// <summary>
/// Represents a host-generated thread event.
/// </summary>
/// <param name="ThreadId">The owning thread id.</param>
/// <param name="Timestamp">The event timestamp.</param>
/// <param name="Kind">The session-update kind.</param>
/// <param name="Message">The user-visible message.</param>
public sealed record WorkThreadHostEvent(
    string ThreadId,
    DateTimeOffset Timestamp,
    AgentSessionUpdateKind Kind,
    string Message)
    : WorkThreadRuntimeEvent(ThreadId, Timestamp);

/// <summary>
/// Wraps a host lifecycle event for a specific thread.
/// </summary>
/// <param name="ThreadId">The owning thread id.</param>
/// <param name="Timestamp">The event timestamp.</param>
/// <param name="Event">The lifecycle event.</param>
public sealed record WorkThreadLifecycleRuntimeEvent(
    string ThreadId,
    DateTimeOffset Timestamp,
    WorkThreadLifecycleEvent Event)
    : WorkThreadRuntimeEvent(ThreadId, Timestamp);
