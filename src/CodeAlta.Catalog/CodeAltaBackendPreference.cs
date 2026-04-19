using CodeAlta.Agent;

namespace CodeAlta.Catalog;

/// <summary>
/// Describes the effective model and reasoning preference for a provider.
/// </summary>
/// <param name="Model">The preferred provider model identifier.</param>
/// <param name="ReasoningEffort">The preferred reasoning effort.</param>
public sealed record CodeAltaProviderPreference(
    string? Model,
    AgentReasoningEffort? ReasoningEffort);
