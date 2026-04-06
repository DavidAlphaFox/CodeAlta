namespace CodeAlta.Agent;

/// <summary>
/// Describes a backend that can be surfaced in the UI and selected for threads.
/// </summary>
/// <param name="BackendId">The backend identifier.</param>
/// <param name="DisplayName">The user-facing backend name.</param>
public sealed record AgentBackendDescriptor(
    AgentBackendId BackendId,
    string DisplayName);
