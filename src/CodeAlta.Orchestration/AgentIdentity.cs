using CodeAlta.Agent;

namespace CodeAlta.Orchestration;

/// <summary>
/// Represents an active backend session owner.
/// </summary>
public sealed record AgentIdentity
{
    /// <summary>
    /// Gets the runtime session owner identifier.
    /// </summary>
    public required AgentId AgentId { get; init; }

    /// <summary>
    /// Gets the backend identifier.
    /// </summary>
    public required AgentBackendId BackendId { get; init; }
}
