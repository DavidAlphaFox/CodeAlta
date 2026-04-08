namespace CodeAlta.Agent.ModelCatalog;

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
