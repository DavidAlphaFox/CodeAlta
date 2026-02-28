namespace CodeAlta.Orchestration;

/// <summary>
/// Represents the scope associated with an agent.
/// </summary>
public sealed record AgentScope
{
    /// <summary>
    /// Gets the scope kind.
    /// </summary>
    public required AgentScopeKind Kind { get; init; }

    /// <summary>
    /// Gets the optional scope identifier (workspace/project id).
    /// </summary>
    public string? Id { get; init; }
}
