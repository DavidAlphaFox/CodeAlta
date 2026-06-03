namespace CodeAlta.Agent;

// 模块功能：描述可在提供者选择器中展示的已配置模型提供者，包含显示名、类型、端点及默认模型等信息
/// <summary>
/// Describes a configured model provider that can be surfaced in provider selectors.
/// </summary>
public sealed record ModelProviderDescriptor
{
    // 函数功能：使用 providerId 与 displayName 构造描述符，providerType 默认回退至 providerId
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelProviderDescriptor" /> class.
    /// </summary>
    /// <param name="providerId">The configured provider identifier.</param>
    /// <param name="displayName">The user-facing provider name. Whitespace falls back to <paramref name="providerId" />.</param>
    public ModelProviderDescriptor(ModelProviderId providerId, string? displayName)
        : this(providerId, displayName, providerId.Value)
    {
    }

    // 函数功能：完整构造函数，支持指定 providerType（如 openai-chat/anthropic），空白值回退至 providerId
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelProviderDescriptor" /> class.
    /// </summary>
    /// <param name="providerId">The configured provider identifier.</param>
    /// <param name="displayName">The user-facing provider name. Whitespace falls back to <paramref name="providerId" />.</param>
    /// <param name="providerType">The provider adapter type, such as <c>openai-chat</c> or <c>anthropic</c>. Whitespace falls back to <paramref name="providerId" />.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerType" /> is empty after fallback.</exception>
    public ModelProviderDescriptor(ModelProviderId providerId, string? displayName, string? providerType)
    {
        ProviderId = providerId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? providerId.Value : displayName.Trim();
        ProviderType = string.IsNullOrWhiteSpace(providerType) ? providerId.Value : providerType.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderType);
    }

    /// <summary>
    /// Gets the configured provider identifier.
    /// </summary>
    public ModelProviderId ProviderId { get; init; }

    /// <summary>
    /// Gets the user-facing provider name.
    /// </summary>
    public string DisplayName { get; init; }

    /// <summary>
    /// Gets the provider adapter type.
    /// </summary>
    public string ProviderType { get; init; }

    /// <summary>
    /// Gets the optional provider endpoint base URI.
    /// </summary>
    public Uri? BaseUri { get; init; }

    /// <summary>
    /// Gets a value indicating whether this provider is the default option within its source definition.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Gets a value indicating whether this provider definition is enabled.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Gets the configured default model identifier, when any.
    /// </summary>
    public string? DefaultModelId { get; init; }

    /// <summary>
    /// Gets the configured default reasoning effort, when any.
    /// </summary>
    public AgentReasoningEffort? DefaultReasoningEffort { get; init; }
}
