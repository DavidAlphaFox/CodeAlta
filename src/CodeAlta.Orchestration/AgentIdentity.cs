using CodeAlta.Agent;
using CodeAlta.Persistence;

namespace CodeAlta.Orchestration;

/// <summary>
/// Represents the logical identity of an orchestrated agent.
/// </summary>
public sealed record AgentIdentity
{
    /// <summary>
    /// Gets the durable agent identifier.
    /// </summary>
    public required AgentId AgentId { get; init; }

    /// <summary>
    /// Gets the role identifier.
    /// </summary>
    public required string RoleId { get; init; }

    /// <summary>
    /// Gets the agent scope.
    /// </summary>
    public required AgentScope Scope { get; init; }

    /// <summary>
    /// Gets the backend identifier.
    /// </summary>
    public required AgentBackendId BackendId { get; init; }
}
