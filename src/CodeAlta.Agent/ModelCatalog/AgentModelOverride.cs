namespace CodeAlta.Agent.ModelCatalog;

// 模块功能：描述应用于已发现模型元数据的本地覆盖配置，可覆盖显示名、Token 限制及能力标志等字段
/// <summary>
/// Describes a local override applied to discovered model metadata.
/// </summary>
public sealed class AgentModelOverride
{
    /// <summary>
    /// Gets or sets an optional replacement display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets an optional replacement description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the context-window limit in tokens.
    /// </summary>
    public long? ContextWindowTokens { get; set; }

    /// <summary>
    /// Gets or sets the maximum input-token limit in tokens.
    /// </summary>
    public long? InputTokenLimit { get; set; }

    /// <summary>
    /// Gets or sets the maximum output-token limit in tokens.
    /// </summary>
    public long? OutputTokenLimit { get; set; }

    /// <summary>
    /// Gets or sets the maximum generated-token limit in tokens.
    /// </summary>
    public long? MaxTokens { get; set; }

    /// <summary>
    /// Gets or sets whether the model supports reasoning output.
    /// </summary>
    public bool? SupportsReasoning { get; set; }

    /// <summary>
    /// Gets or sets whether the model supports tool calling.
    /// </summary>
    public bool? SupportsToolCall { get; set; }

    /// <summary>
    /// Gets or sets whether the model supports attachments.
    /// </summary>
    public bool? SupportsAttachments { get; set; }

    /// <summary>
    /// Gets or sets whether the model supports structured output.
    /// </summary>
    public bool? SupportsStructuredOutput { get; set; }
}
