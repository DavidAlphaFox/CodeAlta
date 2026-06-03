namespace CodeAlta.Agent;

// 模块功能：集中定义所有已知模型提供商的标识符常量
/// <summary>
/// Well-known model provider identifiers.
/// </summary>
public static class ModelProviderIds
{
    // GitHub Copilot 端点提供商标识符
    /// <summary>
    /// GitHub Copilot endpoint provider.
    /// </summary>
    public static readonly ModelProviderId Copilot = new("copilot");

    // Codex 端点提供商标识符
    /// <summary>
    /// Codex endpoint provider.
    /// </summary>
    public static readonly ModelProviderId Codex = new("codex");

    // OpenAI 兼容的 chat/completions 提供商类型标识符
    /// <summary>
    /// OpenAI-compatible chat/completions provider type.
    /// </summary>
    public static readonly ModelProviderId OpenAIChat = new("openai-chat");

    // OpenAI Responses 提供商类型标识符
    /// <summary>
    /// OpenAI Responses provider type.
    /// </summary>
    public static readonly ModelProviderId OpenAIResponses = new("openai-responses");

    // Anthropic Messages 提供商类型标识符
    /// <summary>
    /// Anthropic Messages provider type.
    /// </summary>
    public static readonly ModelProviderId AnthropicMessages = new("anthropic-messages");

    // Google GenAI 提供商类型标识符
    /// <summary>
    /// Google GenAI provider type.
    /// </summary>
    public static readonly ModelProviderId GoogleGenAI = new("google-genai");
}
