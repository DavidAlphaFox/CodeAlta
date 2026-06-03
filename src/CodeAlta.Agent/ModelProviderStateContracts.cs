namespace CodeAlta.Agent;

// 模块功能：定义模型提供者可用性枚举及状态快照/探针结果的数据契约
/// <summary>
/// Describes model-provider readiness.
/// </summary>
public enum ModelProviderAvailability
{
    /// <summary>
    /// The provider state has not been loaded yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// The provider is being probed or initialized.
    /// </summary>
    Probing,

    /// <summary>
    /// The provider is ready for use.
    /// </summary>
    Ready,

    /// <summary>
    /// The provider is configured but disabled.
    /// </summary>
    Disabled,

    /// <summary>
    /// The provider configuration or current host does not support this provider.
    /// </summary>
    Unsupported,

    /// <summary>
    /// The provider failed to initialize or probe.
    /// </summary>
    Failed,
}

// 类型：模型提供者当前状态的不可变快照，包含可用性、模型列表和选中模型等信息
/// <summary>
/// Immutable snapshot of a model provider's current state.
/// </summary>
public sealed record ModelProviderStateSnapshot
{
    /// <summary>
    /// Gets the provider descriptor.
    /// </summary>
    public required ModelProviderDescriptor Descriptor { get; init; }

    /// <summary>
    /// Gets the provider identifier.
    /// </summary>
    public ModelProviderId ProviderId => Descriptor.ProviderId;

    /// <summary>
    /// Gets the current provider availability.
    /// </summary>
    public ModelProviderAvailability Availability { get; init; } = ModelProviderAvailability.Unknown;

    /// <summary>
    /// Gets the user-facing status message.
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Gets the model catalog returned by the provider probe.
    /// </summary>
    public IReadOnlyList<AgentModelInfo> Models { get; init; } = [];

    /// <summary>
    /// Gets the selected or suggested model identifier.
    /// </summary>
    public string? SelectedModelId { get; init; }

    /// <summary>
    /// Gets the selected or suggested reasoning effort.
    /// </summary>
    public AgentReasoningEffort? SelectedReasoningEffort { get; init; }

    /// <summary>
    /// Gets an optional provider-specific error category suitable for diagnostics.
    /// </summary>
    public string? ErrorCategory { get; init; }

    /// <summary>
    /// Gets the time when this state was observed.
    /// </summary>
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}

// 类型：探针调用结果，包含提供者可用性、探测到的模型列表及推荐模型/推理强度
/// <summary>
/// Result of probing a model provider runtime.
/// </summary>
public sealed record ModelProviderProbeResult
{
    /// <summary>
    /// Gets the provider identifier.
    /// </summary>
    public required ModelProviderId ProviderId { get; init; }

    /// <summary>
    /// Gets the probed availability.
    /// </summary>
    public ModelProviderAvailability Availability { get; init; } = ModelProviderAvailability.Ready;

    /// <summary>
    /// Gets the models discovered during probing.
    /// </summary>
    public IReadOnlyList<AgentModelInfo> Models { get; init; } = [];

    /// <summary>
    /// Gets the selected or suggested model identifier.
    /// </summary>
    public string? SelectedModelId { get; init; }

    /// <summary>
    /// Gets the selected or suggested reasoning effort.
    /// </summary>
    public AgentReasoningEffort? SelectedReasoningEffort { get; init; }

    /// <summary>
    /// Gets an optional user-facing status or error message.
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Gets an optional provider-specific error category suitable for diagnostics.
    /// </summary>
    public string? ErrorCategory { get; init; }
}
