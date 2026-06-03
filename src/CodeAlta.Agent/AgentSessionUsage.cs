using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

// 模块功能：会话 token/用量统计数据结构，涵盖上下文窗口快照、最近操作用量、限速摘要及 Copilot/Codex 特定扩展详情。
/// <summary>
/// Represents normalized active-context and token-usage information for a session.
/// </summary>
/// <param name="Window">The current known active input-context usage snapshot when available.</param>
/// <param name="LastOperation">The most recent meaningful operation usage snapshot when available.</param>
/// <param name="RateLimits">The normalized rate-limit summary when available.</param>
/// <param name="Scope">The scope represented by this usage snapshot.</param>
/// <param name="Source">The provider/runtime event source that produced this usage snapshot.</param>
/// <param name="UpdatedAt">The time the usage snapshot was last updated.</param>
/// <param name="Details">Optional provider/runtime-specific usage details.</param>
public sealed record AgentSessionUsage(
    AgentWindowUsageSnapshot? Window = null,
    AgentOperationUsageSnapshot? LastOperation = null,
    AgentRateLimitSummary? RateLimits = null,
    AgentUsageScope Scope = AgentUsageScope.Unknown,
    AgentUsageSource Source = AgentUsageSource.Unknown,
    DateTimeOffset UpdatedAt = default,
    AgentSessionUsageDetails? Details = null)
{
    /// <summary>
    /// Gets the current number of tokens in the active input context when known.
    /// </summary>
    public long? CurrentTokens => Window?.CurrentTokens;

    /// <summary>
    /// Gets the active input-context limit for the active model when known.
    /// </summary>
    public long? TokenLimit => Window?.TokenLimit;

    /// <summary>
    /// Gets the number of messages currently contributing to the active input context when known.
    /// </summary>
    public int? MessageCount => Window?.MessageCount;

    /// <summary>
    /// Gets the percentage of the active input context currently in use when both values are available.
    /// </summary>
    public double? WindowUsagePercentage =>
        Window?.TokenLimit is > 0 && Window.CurrentTokens is >= 0
            ? (Window.CurrentTokens.Value * 100d) / Window.TokenLimit.Value
            : null;
}

// 类型：当前活跃输入上下文的用量快照，包含已用 token 数、token 上限、消息数量及相关标签。
/// <summary>
/// Represents a normalized active input-context usage snapshot.
/// </summary>
/// <param name="CurrentTokens">The current number of tokens in the active input context when known.</param>
/// <param name="TokenLimit">The active input-context limit when known.</param>
/// <param name="MessageCount">The number of messages currently contributing to the active input context when known.</param>
/// <param name="Label">The UI label describing the window snapshot.</param>
/// <param name="TotalContextEnvelope">The optional advertised input-plus-output model envelope.</param>
/// <param name="MaxOutputTokens">The optional maximum output-token limit.</param>
public sealed record AgentWindowUsageSnapshot(
    long? CurrentTokens,
    long? TokenLimit,
    int? MessageCount,
    string? Label = null,
    long? TotalContextEnvelope = null,
    long? MaxOutputTokens = null);

// 类型：最近一次有效模型操作的规范化用量快照，含 token 消耗、费用、耗时、发起方及推理 effort 等信息。
/// <summary>
/// Represents normalized usage for the most recent meaningful model operation.
/// </summary>
/// <param name="Model">The model that serviced the operation when known.</param>
/// <param name="InputTokens">The number of fresh input tokens consumed.</param>
/// <param name="OutputTokens">The number of output tokens produced.</param>
/// <param name="CacheReadTokens">The number of tokens read from prompt cache when known.</param>
/// <param name="CacheWriteTokens">The number of tokens written to prompt cache when known.</param>
/// <param name="CachedInputTokens">The number of cached input tokens reused when known.</param>
/// <param name="ReasoningTokens">The number of reasoning tokens consumed or produced when known.</param>
/// <param name="Cost">The provider-reported cost when available.</param>
/// <param name="DurationMs">The provider-reported duration in milliseconds when available.</param>
/// <param name="Initiator">The initiator of the operation when reported.</param>
/// <param name="ParentToolCallId">The parent tool call identifier when this operation belongs to a sub-agent or tool request.</param>
/// <param name="ReasoningEffort">The reasoning-effort setting used for the operation when reported.</param>
/// <param name="Label">The UI label describing the operation snapshot.</param>
public sealed record AgentOperationUsageSnapshot(
    string? Model = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    long? CacheReadTokens = null,
    long? CacheWriteTokens = null,
    long? CachedInputTokens = null,
    long? ReasoningTokens = null,
    double? Cost = null,
    double? DurationMs = null,
    string? Initiator = null,
    string? ParentToolCallId = null,
    string? ReasoningEffort = null,
    string? Label = null);

// 类型：规范化限速摘要，包含限额名称、计划类型及主/次限速窗口。
/// <summary>
/// Represents normalized rate-limit information for a session.
/// </summary>
/// <param name="Name">The rate-limit or quota display name when known.</param>
/// <param name="PlanType">The active plan type when known.</param>
/// <param name="Primary">The primary rate-limit window when available.</param>
/// <param name="Secondary">The secondary rate-limit window when available.</param>
/// <param name="Label">The UI label describing the rate-limit snapshot.</param>
public sealed record AgentRateLimitSummary(
    string? Name = null,
    string? PlanType = null,
    AgentRateLimitWindow? Primary = null,
    AgentRateLimitWindow? Secondary = null,
    string? Label = null);

// 类型：规范化限速窗口，记录已用百分比、重置时间及窗口时长（分钟）。
/// <summary>
/// Represents a normalized rate-limit window.
/// </summary>
/// <param name="UsedPercent">The percentage of the window currently consumed when known.</param>
/// <param name="ResetsAt">The time the window resets when known.</param>
/// <param name="WindowDurationMinutes">The duration of the rate-limit window in minutes when known.</param>
public sealed record AgentRateLimitWindow(
    int? UsedPercent = null,
    DateTimeOffset? ResetsAt = null,
    long? WindowDurationMinutes = null);

// 类型：枚举，标识用量快照所代表的语义范围（当前窗口、最近操作、会话累计、压缩、截断或纯限速）。
/// <summary>
/// Identifies the semantic scope represented by a usage snapshot.
/// </summary>
public enum AgentUsageScope
{
    /// <summary>
    /// The scope is unknown.
    /// </summary>
    Unknown,

    /// <summary>
    /// The snapshot represents the current context window.
    /// </summary>
    CurrentWindow,

    /// <summary>
    /// The snapshot represents the most recent model operation.
    /// </summary>
    LastOperation,

    /// <summary>
    /// The snapshot represents cumulative session totals.
    /// </summary>
    SessionTotal,

    /// <summary>
    /// The snapshot represents compaction.
    /// </summary>
    Compaction,

    /// <summary>
    /// The snapshot represents truncation.
    /// </summary>
    Truncation,

    /// <summary>
    /// The snapshot only contains rate-limit data.
    /// </summary>
    RateLimitOnly
}

// 类型：枚举，标识产生该用量快照的 provider 事件来源（Copilot/Codex 各类事件或历史恢复/本地 provider）。
/// <summary>
/// Identifies which provider event produced a usage snapshot.
/// </summary>
public enum AgentUsageSource
{
    /// <summary>
    /// The source is unknown.
    /// </summary>
    Unknown,

    /// <summary>
    /// Copilot session usage information.
    /// </summary>
    CopilotSessionUsageInfo,

    /// <summary>
    /// Copilot assistant usage.
    /// </summary>
    CopilotAssistantUsage,

    /// <summary>
    /// Copilot account quota snapshots fetched explicitly from the provider.
    /// </summary>
    CopilotAccountQuota,

    /// <summary>
    /// Copilot session compaction completion.
    /// </summary>
    CopilotCompactionComplete,

    /// <summary>
    /// Copilot session truncation.
    /// </summary>
    CopilotTruncation,

    /// <summary>
    /// Codex session token usage updates.
    /// </summary>
    CodexSessionTokenUsageUpdated,

    /// <summary>
    /// Codex token-count events.
    /// </summary>
    CodexTokenCountEvent,

    /// <summary>
    /// Codex account rate-limit updates.
    /// </summary>
    CodexAccountRateLimitsUpdated,

    /// <summary>
    /// Usage data recovered from persisted state or history.
    /// </summary>
    RecoveredHistory,

    /// <summary>
    /// Live usage data reported by a provider turn executor.
    /// </summary>
    [JsonStringEnumMemberName("LocalProviderUsage")]
    ProviderUsage
}

// 类型：provider/运行时特定用量详情的多态基类，由 JSON 鉴别符 "$type" 区分 codex 和 copilot 派生类型。
/// <summary>
/// Base type for provider/runtime-specific session usage information.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CodexSessionUsageDetails), "codex")]
[JsonDerivedType(typeof(CopilotSessionUsageDetails), "copilot")]
public abstract record AgentSessionUsageDetails;

// 类型：Codex 特定用量详情，包含最近一轮及累计 token 用量、上下文窗口大小和限速快照。
/// <summary>
/// Codex-specific usage details.
/// </summary>
/// <param name="LastTurnUsage">Token usage for the most recent turn when available.</param>
/// <param name="TotalUsage">Cumulative session token usage when available.</param>
/// <param name="ModelContextWindow">The model context-window size reported by Codex when available.</param>
/// <param name="RateLimits">The latest Codex account rate-limit snapshot when available.</param>
public sealed record CodexSessionUsageDetails(
    CodexTokenUsage? LastTurnUsage = null,
    CodexTokenUsage? TotalUsage = null,
    long? ModelContextWindow = null,
    CodexRateLimitSnapshot? RateLimits = null)
    : AgentSessionUsageDetails;

// 类型：Copilot 特定用量详情，包含最近模型调用用量、最近压缩信息及配额快照列表。
/// <summary>
/// Copilot-specific usage details.
/// </summary>
/// <param name="LastAssistantUsage">Usage for the most recent Copilot model call when available.</param>
/// <param name="LastCompaction">Usage and reduction details for the most recent compaction when available.</param>
/// <param name="QuotaSnapshots">Typed Copilot quota snapshots when available.</param>
public sealed record CopilotSessionUsageDetails(
    CopilotAssistantUsage? LastAssistantUsage = null,
    CopilotCompactionUsage? LastCompaction = null,
    CopilotQuotaSnapshot[]? QuotaSnapshots = null)
    : AgentSessionUsageDetails;

// 类型：Codex token 用量细分，区分缓存输入、新鲜输入、输出、推理输出及总量。
/// <summary>
/// Codex token-usage breakdown.
/// </summary>
/// <param name="CachedInputTokens">The number of cached input tokens reused.</param>
/// <param name="InputTokens">The number of fresh input tokens consumed.</param>
/// <param name="OutputTokens">The number of output tokens produced.</param>
/// <param name="ReasoningOutputTokens">The number of reasoning output tokens produced.</param>
/// <param name="TotalTokens">The total number of tokens represented by the breakdown.</param>
public sealed record CodexTokenUsage(
    long CachedInputTokens,
    long InputTokens,
    long OutputTokens,
    long ReasoningOutputTokens,
    long TotalTokens);

// 类型：Codex 限速快照，含限额 ID、名称、计划类型及主/次限速窗口。
/// <summary>
/// Codex rate-limit snapshot.
/// </summary>
/// <param name="LimitId">The provider-specific limit identifier.</param>
/// <param name="LimitName">The provider-specific limit display name.</param>
/// <param name="PlanType">The active Codex plan type when known.</param>
/// <param name="Primary">The primary rate-limit window when available.</param>
/// <param name="Secondary">The secondary rate-limit window when available.</param>
public sealed record CodexRateLimitSnapshot(
    string? LimitId,
    string? LimitName,
    string? PlanType,
    CodexRateLimitWindow? Primary,
    CodexRateLimitWindow? Secondary);

// 类型：Codex 限速窗口详情，包含已用百分比、重置时间和窗口时长。
/// <summary>
/// Codex rate-limit window details.
/// </summary>
/// <param name="UsedPercent">The percentage of the window currently consumed.</param>
/// <param name="ResetsAt">The time the window resets when known.</param>
/// <param name="WindowDurationMinutes">The rate-limit window duration in minutes when known.</param>
public sealed record CodexRateLimitWindow(
    int UsedPercent,
    DateTimeOffset? ResetsAt,
    long? WindowDurationMinutes);

// 类型：Copilot 模型调用的 token 和计费用量，含输入/输出 token、缓存读写、费用、耗时及 AIU 成本。
/// <summary>
/// Copilot assistant-call token and billing usage.
/// </summary>
/// <param name="Model">The model that serviced the call.</param>
/// <param name="InputTokens">The number of input tokens consumed.</param>
/// <param name="OutputTokens">The number of output tokens produced.</param>
/// <param name="CacheReadTokens">The number of tokens read from prompt cache.</param>
/// <param name="CacheWriteTokens">The number of tokens written to prompt cache.</param>
/// <param name="Cost">The provider-reported request cost when available.</param>
/// <param name="DurationMs">The provider-reported request duration in milliseconds when available.</param>
/// <param name="Initiator">The initiator for the call when reported.</param>
/// <param name="ParentToolCallId">The parent tool call identifier when this usage belongs to a sub-agent or tool request.</param>
/// <param name="ReasoningEffort">The reasoning-effort setting used for the request when reported.</param>
/// <param name="TotalNanoAiu">The Copilot AIU cost reported by the provider when available.</param>
/// <param name="TokenDetails">Additional provider token details when available.</param>
public sealed record CopilotAssistantUsage(
    string Model,
    long? InputTokens = null,
    long? OutputTokens = null,
    long? CacheReadTokens = null,
    long? CacheWriteTokens = null,
    double? Cost = null,
    double? DurationMs = null,
    string? Initiator = null,
    string? ParentToolCallId = null,
    string? ReasoningEffort = null,
    double? TotalNanoAiu = null,
    CopilotTokenDetail[]? TokenDetails = null);

// 类型：Copilot token 分类明细条目，记录某一类别的 token 名称和数量。
/// <summary>
/// Copilot token-detail entry.
/// </summary>
/// <param name="TokenType">The token category label.</param>
/// <param name="TokenCount">The number of tokens in that category.</param>
public sealed record CopilotTokenDetail(
    string TokenType,
    long TokenCount);

// 类型：Copilot 压缩操作的用量与缩减详情，记录压缩前后 token/消息数量、移除量及压缩摘要。
/// <summary>
/// Copilot compaction usage and reduction details.
/// </summary>
/// <param name="Success">Whether the compaction completed successfully.</param>
/// <param name="PreCompactionTokens">The token count before compaction when reported.</param>
/// <param name="PostCompactionTokens">The token count after compaction when reported.</param>
/// <param name="PreCompactionMessages">The message count before compaction when reported.</param>
/// <param name="MessagesRemoved">The number of messages removed by compaction when reported.</param>
/// <param name="TokensRemoved">The number of tokens removed by compaction when reported.</param>
/// <param name="TokensUsed">The token usage for the compaction model call when reported.</param>
/// <param name="SummaryContent">The provider-provided compaction summary when reported.</param>
public sealed record CopilotCompactionUsage(
    bool Success,
    long? PreCompactionTokens = null,
    long? PostCompactionTokens = null,
    int? PreCompactionMessages = null,
    int? MessagesRemoved = null,
    long? TokensRemoved = null,
    CopilotCompactionTokenUsage? TokensUsed = null,
    string? SummaryContent = null);

// 类型：压缩 LLM 调用的 token 用量，包含输入、输出及缓存复用的 token 数量。
/// <summary>
/// Copilot token usage for the compaction LLM call.
/// </summary>
/// <param name="InputTokens">The number of input tokens consumed.</param>
/// <param name="OutputTokens">The number of output tokens produced.</param>
/// <param name="CachedInputTokens">The number of cached input tokens reused.</param>
public sealed record CopilotCompactionTokenUsage(
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens);

// 类型：具名 Copilot 配额快照，关联配额标识符与具体的配额详情。
/// <summary>
/// Named Copilot quota snapshot.
/// </summary>
/// <param name="Name">The quota identifier.</param>
/// <param name="Details">The typed quota details.</param>
public sealed record CopilotQuotaSnapshot(
    string Name,
    CopilotQuotaDetails Details);

// 类型：Copilot 配额详情的多态基类，由 "$type" 鉴别符区分 request（请求配额）和 opaque（不透明配额）子类型。
/// <summary>
/// Base type for typed Copilot quota details.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CopilotRequestQuotaDetails), "request")]
[JsonDerivedType(typeof(CopilotOpaqueQuotaDetails), "opaque")]
public abstract record CopilotQuotaDetails;

// 类型：Copilot 请求配额快照，记录权益请求数、已用数、剩余百分比、超额量及配额重置时间。
/// <summary>
/// Typed Copilot request-quota snapshot.
/// </summary>
/// <param name="EntitlementRequests">The number of requests included in the entitlement when known.</param>
/// <param name="UsedRequests">The number of requests used in the current period when known.</param>
/// <param name="RemainingPercentage">The percentage of the entitlement remaining when known.</param>
/// <param name="Overage">The number of overage requests when known.</param>
/// <param name="UsageAllowedWithExhaustion">Whether usage continues when the quota is exhausted.</param>
/// <param name="IsUnlimitedEntitlement">Whether the quota is effectively unlimited.</param>
/// <param name="ResetDate">The time the quota resets when known.</param>
public sealed record CopilotRequestQuotaDetails(
    long? EntitlementRequests = null,
    long? UsedRequests = null,
    double? RemainingPercentage = null,
    long? Overage = null,
    bool? UsageAllowedWithExhaustion = null,
    bool? IsUnlimitedEntitlement = null,
    DateTimeOffset? ResetDate = null)
    : CopilotQuotaDetails;

// 类型：不透明 Copilot 配额快照兜底类型，将未知结构序列化为文本摘要。
/// <summary>
/// Typed opaque Copilot quota snapshot fallback for unknown shapes.
/// </summary>
/// <param name="Summary">A compact text summary of the unknown payload.</param>
public sealed record CopilotOpaqueQuotaDetails(
    string Summary)
    : CopilotQuotaDetails;
