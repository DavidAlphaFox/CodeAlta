using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

// 模块功能：描述提供商中某个可用模型的基本信息，包括推理能力和能力元数据
/// <summary>
/// Describes an available model in an agent provider.
/// </summary>
/// <param name="Id">The provider model identifier.</param>
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
