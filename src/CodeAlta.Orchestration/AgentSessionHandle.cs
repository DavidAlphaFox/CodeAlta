using CodeAlta.Agent;

namespace CodeAlta.Orchestration;

/// <summary>
/// Describes an active in-memory session attachment owned by the agent runtime facade.
/// </summary>
public sealed record AgentSessionHandle
{
    /// <summary>
    /// Gets the transient in-memory handle for the active session attachment.
    /// </summary>
    public required AgentSessionHandleId HandleId { get; init; }

    /// <summary>
    /// Gets the durable CodeAlta session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the configured provider identifier used to create or resume the session attachment.
    /// </summary>
    public required AgentBackendId ProviderId { get; init; }

    /// <summary>
    /// Gets the optional durable parent session identifier used for coordination metadata only.
    /// </summary>
    public string? ParentSessionId { get; init; }
}
