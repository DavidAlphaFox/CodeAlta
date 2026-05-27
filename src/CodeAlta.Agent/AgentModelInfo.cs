using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

/// <summary>
/// Describes an available model in an agent backend.
/// </summary>
/// <param name="Id">The backend model identifier.</param>
/// <param name="DisplayName">Optional display name.</param>
/// <param name="Description">Optional model description.</param>
/// <param name="Provider">Optional provider identifier (e.g. "openai").</param>
/// <param name="DefaultReasoningEffort">Optional default reasoning effort for this model.</param>
/// <param name="SupportedReasoningEfforts">Optional set of reasoning effort values supported by this model.</param>
/// <param name="Capabilities">
/// Optional provider-defined capabilities metadata. The shape is provider-specific.
/// </param>
public sealed record AgentModelInfo(
    string Id,
    string? DisplayName = null,
    string? Description = null,
    string? Provider = null,
    AgentReasoningEffort? DefaultReasoningEffort = null,
    IReadOnlyList<AgentReasoningEffort>? SupportedReasoningEfforts = null,
    [property: JsonConverter(typeof(AgentObjectDictionaryJsonConverter))]
    IReadOnlyDictionary<string, object?>? Capabilities = null);
