namespace CodeAlta.Agent;

/// <summary>
/// Options for steering an already running agent turn.
/// </summary>
public sealed class AgentSteerOptions
{
    /// <summary>
    /// Gets or initializes the input to append to the active run.
    /// </summary>
    public required AgentInput Input { get; init; }

    /// <summary>
    /// Gets or initializes the expected active run identifier.
    /// </summary>
    /// <remarks>
    /// When specified, the adapter should only steer the matching in-flight run.
    /// When omitted, the adapter may use its current active run if the backend supports that behavior.
    /// </remarks>
    public AgentRunId? ExpectedRunId { get; init; }
}
