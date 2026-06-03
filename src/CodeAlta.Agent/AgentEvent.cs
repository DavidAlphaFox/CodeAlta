using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

// 模块功能：规范化的多态 agent 事件模型，作为所有 agent 事件的抽象基类，通过 JSON 多态派发支持多种具体事件类型
/// <summary>
/// Base type for a normalized agent event.
/// </summary>
/// <param name="ProviderId">The model provider identifier. Serialized as <c>backendId</c> for session-journal compatibility.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AgentRawEvent), "raw")]
[JsonDerivedType(typeof(AgentContentDeltaEvent), "contentDelta")]
[JsonDerivedType(typeof(AgentContentCompletedEvent), "contentCompleted")]
[JsonDerivedType(typeof(AgentActivityEvent), "activity")]
[JsonDerivedType(typeof(AgentSystemPromptEvent), "system_prompt")]
[JsonDerivedType(typeof(AgentSessionUpdateEvent), "sessionUpdate")]
[JsonDerivedType(typeof(AgentPlanSnapshotEvent), "planSnapshot")]
[JsonDerivedType(typeof(AgentInteractionEvent), "interaction")]
[JsonDerivedType(typeof(AgentErrorEvent), "error")]
[JsonDerivedType(typeof(AgentGenericPermissionRequest), "permissionGeneric")]
[JsonDerivedType(typeof(AgentCommandPermissionRequest), "permissionCommand")]
[JsonDerivedType(typeof(AgentFileChangePermissionRequest), "permissionFileChange")]
[JsonDerivedType(typeof(AgentUserInputRequest), "userInputRequest")]
public abstract record AgentEvent(
    [property: JsonPropertyName("backendId")]
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId = null);

// 类型：内容通道标识枚举，区分流式或完成内容事件所属的内容类型
/// <summary>
/// Identifies the content channel for a streamed or completed content event.
/// </summary>
public enum AgentContentKind
{
    /// <summary>
    /// User-authored prompt text.
    /// </summary>
    User,

    /// <summary>
    /// Assistant/user-facing response text.
    /// </summary>
    Assistant,

    /// <summary>
    /// Reasoning text emitted by the model.
    /// </summary>
    Reasoning,

    /// <summary>
    /// Reasoning summary text emitted by the provider.
    /// </summary>
    ReasoningSummary,

    /// <summary>
    /// Plan text emitted by the provider.
    /// </summary>
    Plan,

    /// <summary>
    /// Command execution output text.
    /// </summary>
    CommandOutput,

    /// <summary>
    /// File-change output text.
    /// </summary>
    FileChangeOutput,

    /// <summary>
    /// Tool output text.
    /// </summary>
    ToolOutput,

    /// <summary>
    /// Notice-like content that should still render in the timeline.
    /// </summary>
    Notice,
}

// 类型：操作生命周期活动通道标识枚举，区分工具调用、命令执行等各类活动类型
/// <summary>
/// Identifies the activity channel for operation lifecycle events.
/// </summary>
public enum AgentActivityKind
{
    /// <summary>
    /// A turn/run lifecycle event.
    /// </summary>
    Turn,

    /// <summary>
    /// A generic tool call lifecycle event.
    /// </summary>
    ToolCall,

    /// <summary>
    /// A command execution lifecycle event.
    /// </summary>
    CommandExecution,

    /// <summary>
    /// A file-change lifecycle event.
    /// </summary>
    FileChange,

    /// <summary>
    /// An MCP tool call lifecycle event.
    /// </summary>
    McpToolCall,

    /// <summary>
    /// A dynamic tool call lifecycle event.
    /// </summary>
    DynamicToolCall,

    /// <summary>
    /// A collaboration/sub-agent tool call lifecycle event.
    /// </summary>
    CollabAgentToolCall,

    /// <summary>
    /// A subagent lifecycle event.
    /// </summary>
    Subagent,

    /// <summary>
    /// A hook lifecycle event.
    /// </summary>
    Hook,

    /// <summary>
    /// A skill lifecycle event.
    /// </summary>
    Skill,

    /// <summary>
    /// A compaction lifecycle event.
    /// </summary>
    Compaction,

    /// <summary>
    /// A web search lifecycle event.
    /// </summary>
    WebSearch,

    /// <summary>
    /// An image generation lifecycle event.
    /// </summary>
    ImageGeneration,
}

// 类型：活动生命周期阶段枚举，表示活动从请求到完成/失败/取消的各阶段
/// <summary>
/// Identifies the phase of an activity lifecycle event.
/// </summary>
public enum AgentActivityPhase
{
    /// <summary>
    /// The activity has been requested but not yet started.
    /// </summary>
    Requested,

    /// <summary>
    /// The activity has started.
    /// </summary>
    Started,

    /// <summary>
    /// The activity emitted progress.
    /// </summary>
    Progressed,

    /// <summary>
    /// The activity completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The activity failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The activity was canceled.
    /// </summary>
    Canceled,

    /// <summary>
    /// A selectable activity or agent was selected.
    /// </summary>
    Selected,

    /// <summary>
    /// A selectable activity or agent was deselected.
    /// </summary>
    Deselected,
}

// 类型：会话更新事件类型枚举，涵盖会话启动、空闲、模型切换、用量更新等状态
/// <summary>
/// Identifies the kind of a session update event.
/// </summary>
public enum AgentSessionUpdateKind
{
    /// <summary>
    /// The session started.
    /// </summary>
    Started,

    /// <summary>
    /// The session resumed.
    /// </summary>
    Resumed,

    /// <summary>
    /// The session became idle.
    /// </summary>
    Idle,

    /// <summary>
    /// Informational session update.
    /// </summary>
    Info,

    /// <summary>
    /// Warning session update.
    /// </summary>
    Warning,

    /// <summary>
    /// The provider stream is reconnecting or retrying after a transient failure.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Model changed or was rerouted.
    /// </summary>
    ModelChanged,

    /// <summary>
    /// Session mode changed.
    /// </summary>
    ModeChanged,

    /// <summary>
    /// Session title changed.
    /// </summary>
    TitleChanged,

    /// <summary>
    /// Session context changed.
    /// </summary>
    ContextChanged,

    /// <summary>
    /// Plan state changed.
    /// </summary>
    PlanUpdated,

    /// <summary>
    /// Usage information changed.
    /// </summary>
    UsageUpdated,

    /// <summary>
    /// Compaction started.
    /// </summary>
    CompactionStarted,

    /// <summary>
    /// Compaction completed.
    /// </summary>
    CompactionCompleted,

    /// <summary>
    /// Session handoff occurred.
    /// </summary>
    Handoff,

    /// <summary>
    /// Session truncation occurred.
    /// </summary>
    Truncated,

    /// <summary>
    /// Session shutdown occurred.
    /// </summary>
    Shutdown,

    /// <summary>
    /// A task completed.
    /// </summary>
    TaskCompleted,

    /// <summary>
    /// A diff or patch preview changed.
    /// </summary>
    DiffUpdated,
}

// 类型：通用交互生命周期事件类型枚举，标识权限解析或用户输入解析
/// <summary>
/// Identifies the kind of a generic interaction lifecycle event.
/// </summary>
public enum AgentInteractionKind
{
    /// <summary>
    /// A permission request was resolved.
    /// </summary>
    PermissionResolved,

    /// <summary>
    /// A user-input request was resolved.
    /// </summary>
    UserInputResolved,
}

// 类型：计划变更类型枚举，标识计划被创建、更新或删除
/// <summary>
/// Identifies how a plan changed.
/// </summary>
public enum AgentPlanChangeKind
{
    /// <summary>
    /// The plan was created.
    /// </summary>
    Created,

    /// <summary>
    /// The plan was updated.
    /// </summary>
    Updated,

    /// <summary>
    /// The plan was deleted.
    /// </summary>
    Deleted,
}

// 类型：计划步骤状态枚举，标识步骤是否待执行、进行中或已完成
/// <summary>
/// Identifies the status of a structured plan step.
/// </summary>
public enum AgentPlanStepStatus
{
    /// <summary>
    /// The step is pending.
    /// </summary>
    Pending,

    /// <summary>
    /// The step is in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// The step completed.
    /// </summary>
    Completed,
}

// 类型：原始提供商事件，当无法映射到标准事件时携带未经处理的 provider 负载
/// <summary>
/// A raw, provider-specific event emitted when no normalized mapping exists.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="BackendEventType">Provider raw-event type identifier.</param>
/// <param name="Raw">Raw provider payload.</param>
/// <param name="RunId">Optional run identifier.</param>
public sealed record AgentRawEvent(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    string BackendEventType,
    JsonElement Raw,
    AgentRunId? RunId = null)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

// 类型：流式内容增量事件，携带标准化内容通道的实时流片段
/// <summary>
/// Streaming delta of normalized content.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="Kind">The content channel.</param>
/// <param name="ContentId">Stable content identifier.</param>
/// <param name="ParentActivityId">Optional parent activity identifier.</param>
/// <param name="Delta">Delta content.</param>
/// <param name="Details">Optional structured content metadata.</param>
public sealed record AgentContentDeltaEvent(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    AgentContentKind Kind,
    string ContentId,
    string? ParentActivityId,
    string Delta,
    JsonElement? Details = null)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

// 类型：内容完成事件，携带某个内容通道的最终完整内容
/// <summary>
/// Finalized normalized content.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="Kind">The content channel.</param>
/// <param name="ContentId">Stable content identifier.</param>
/// <param name="ParentActivityId">Optional parent activity identifier.</param>
/// <param name="Content">The finalized content.</param>
/// <param name="Details">Optional structured content metadata.</param>
/// <param name="AskId">Optional ask identifier associated with a user prompt.</param>
public sealed record AgentContentCompletedEvent(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    AgentContentKind Kind,
    string ContentId,
    string? ParentActivityId,
    string Content,
    JsonElement? Details = null,
    [property: JsonPropertyName("ask_id")] string? AskId = null)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

// 类型：通用活动生命周期事件，涵盖工具调用、命令、文件变更等操作的各阶段通知
/// <summary>
/// Generic activity lifecycle event.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="Kind">The activity channel.</param>
/// <param name="Phase">The lifecycle phase.</param>
/// <param name="ActivityId">Stable activity identifier.</param>
/// <param name="ParentActivityId">Optional parent activity identifier.</param>
/// <param name="Name">Optional activity display name.</param>
/// <param name="Message">Optional activity message.</param>
/// <param name="Details">Optional structured details.</param>
public sealed record AgentActivityEvent(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    AgentActivityKind Kind,
    AgentActivityPhase Phase,
    string ActivityId,
    string? ParentActivityId,
    string? Name,
    string? Message,
    JsonElement? Details = null)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

// 类型：可审计的系统 Prompt 事件，记录每次轮次实际使用的系统提示内容及变更摘要
/// <summary>
/// Auditable system prompt event containing the logical prompt applied to a session turn.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="Reason">Reason the prompt event was emitted.</param>
/// <param name="EffectivePromptHash">Stable effective prompt hash.</param>
/// <param name="SystemMessage">Logical system message.</param>
/// <param name="DeveloperInstructions">Logical developer instructions.</param>
/// <param name="ProviderPayloadSummary">Provider payload summary.</param>
/// <param name="Manifest">Prompt manifest.</param>
/// <param name="Statistics">Prompt statistics.</param>
/// <param name="Change">Change summary compared with the previous prompt event.</param>
public sealed record AgentSystemPromptEvent(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    string Reason,
    string EffectivePromptHash,
    string? SystemMessage,
    string? DeveloperInstructions,
    AgentSystemPromptProviderPayloadSummary ProviderPayloadSummary,
    JsonElement? Manifest,
    AgentSystemPromptStatistics Statistics,
    AgentSystemPromptChangeSummary Change)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

// 类型：系统 Prompt 事件的 provider 负载映射摘要，描述通道映射方式及是否有损
/// <summary>
/// Provider payload mapping summary for a system prompt event.
/// </summary>
/// <param name="ChannelMapping">Channel mapping label.</param>
/// <param name="AppliedToProvider">Whether the prompt was applied to the provider request.</param>
/// <param name="Lossy">Whether the mapping was lossy.</param>
public sealed record AgentSystemPromptProviderPayloadSummary(string ChannelMapping, bool AppliedToProvider, bool Lossy);

// 类型：系统 Prompt 近似统计数据，包含 token 数量和字符数统计
/// <summary>
/// Approximate system prompt statistics.
/// </summary>
/// <param name="SystemApproxTokens">Approximate system token count.</param>
/// <param name="DeveloperApproxTokens">Approximate developer token count.</param>
/// <param name="TotalApproxTokens">Approximate total token count.</param>
/// <param name="SystemChars">System character count.</param>
/// <param name="DeveloperChars">Developer character count.</param>
public sealed record AgentSystemPromptStatistics(int SystemApproxTokens, int DeveloperApproxTokens, int TotalApproxTokens, int SystemChars, int DeveloperChars);

// 类型：系统 Prompt 变更摘要，记录与上一次事件相比新增、删除、修改的 Prompt 部分
/// <summary>
/// Change summary for a system prompt event.
/// </summary>
/// <param name="Kind">Change kind.</param>
/// <param name="AddedParts">Added prompt part keys.</param>
/// <param name="RemovedParts">Removed prompt part keys.</param>
/// <param name="ChangedParts">Changed prompt part keys.</param>
public sealed record AgentSystemPromptChangeSummary(string Kind, IReadOnlyList<string> AddedParts, IReadOnlyList<string> RemovedParts, IReadOnlyList<string> ChangedParts);

// 类型：通用会话更新事件，携带会话状态变化的类型、消息及结构化详情
/// <summary>
/// Generic session update event.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="Kind">The session update kind.</param>
/// <param name="Message">Optional update message.</param>
/// <param name="Details">Optional structured details.</param>
/// <param name="Usage">Optional normalized usage payload.</param>
public sealed record AgentSessionUpdateEvent(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    AgentSessionUpdateKind Kind,
    string? Message,
    JsonElement? Details = null,
    AgentSessionUsage? Usage = null)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

// 类型：结构化计划快照事件，携带当前计划的完整步骤列表和说明
/// <summary>
/// Structured plan snapshot event.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="Snapshot">The structured plan snapshot.</param>
public sealed record AgentPlanSnapshotEvent(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    AgentPlanSnapshot Snapshot)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

// 类型：计划快照负载，包含变更类型、计划说明文本及各步骤列表
/// <summary>
/// Structured plan snapshot payload.
/// </summary>
/// <param name="ChangeKind">Optional change kind.</param>
/// <param name="Explanation">Optional plan explanation.</param>
/// <param name="Steps">Optional plan steps.</param>
public sealed record AgentPlanSnapshot(
    AgentPlanChangeKind? ChangeKind,
    string? Explanation,
    IReadOnlyList<AgentPlanStep>? Steps);

// 类型：计划步骤负载，包含步骤文本和可选状态
/// <summary>
/// Structured plan step payload.
/// </summary>
/// <param name="Text">The step text.</param>
/// <param name="Status">The optional step status.</param>
public sealed record AgentPlanStep(
    string Text,
    AgentPlanStepStatus? Status);

// 类型：通用交互生命周期事件，标识权限请求或用户输入请求的解析结果
/// <summary>
/// Generic interaction lifecycle event.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="Kind">The interaction kind.</param>
/// <param name="InteractionId">Stable interaction identifier.</param>
/// <param name="Message">Optional interaction message.</param>
/// <param name="Details">Optional structured details.</param>
public sealed record AgentInteractionEvent(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    AgentInteractionKind Kind,
    string InteractionId,
    string? Message,
    JsonElement? Details = null)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

// 类型：错误事件，携带错误消息及可选的异常信息，支持 JSON 序列化
/// <summary>
/// Represents an error event.
/// </summary>
public sealed record AgentErrorEvent : AgentEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentErrorEvent"/> class.
    /// </summary>
    /// <param name="ProviderId">The model provider identifier.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="timestamp">Event timestamp.</param>
    /// <param name="message">Error message.</param>
    /// <param name="exceptionInfo">Optional JSON-safe exception payload.</param>
    /// <param name="runId">Optional run identifier.</param>
    [JsonConstructor]
    internal AgentErrorEvent(
        ModelProviderId ProviderId,
        string sessionId,
        DateTimeOffset timestamp,
        string message,
        AgentExceptionInfo? exceptionInfo = null,
        AgentRunId? runId = null)
        : base(ProviderId, sessionId, timestamp, runId)
    {
        Message = message;
        ExceptionInfo = exceptionInfo;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentErrorEvent"/> class.
    /// </summary>
    /// <param name="ProviderId">The model provider identifier.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="timestamp">Event timestamp.</param>
    /// <param name="message">Error message.</param>
    /// <param name="exception">Optional runtime exception.</param>
    /// <param name="runId">Optional run identifier.</param>
    public AgentErrorEvent(
        ModelProviderId ProviderId,
        string sessionId,
        DateTimeOffset timestamp,
        string message,
        Exception? exception = null,
        AgentRunId? runId = null)
        : base(ProviderId, sessionId, timestamp, runId)
    {
        Message = message;
        Exception = exception;
        ExceptionInfo = exception is null ? null : AgentExceptionInfo.FromException(exception);
    }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// Gets the optional runtime exception.
    /// </summary>
    [JsonIgnore]
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets a JSON-safe exception payload for display and diagnostics.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentExceptionInfo? ExceptionInfo { get; init; }
}

// 类型：JSON 安全的异常信息载体，用于序列化传输异常类型、消息、堆栈等诊断数据
/// <summary>
/// JSON-safe exception payload.
/// </summary>
/// <param name="Type">The exception type name.</param>
/// <param name="Message">The exception message.</param>
/// <param name="StackTrace">Optional stack trace.</param>
/// <param name="Source">Optional exception source.</param>
/// <param name="HResult">Optional HRESULT.</param>
/// <param name="InnerException">Optional inner exception payload.</param>
public sealed record AgentExceptionInfo(
    string Type,
    string Message,
    string? StackTrace = null,
    string? Source = null,
    int? HResult = null,
    AgentExceptionInfo? InnerException = null)
{
    // 函数功能：将运行时异常递归转换为 JSON 安全的 AgentExceptionInfo，包含内部异常链
    internal static AgentExceptionInfo FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new AgentExceptionInfo(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace,
            exception.Source,
            exception.HResult,
            exception.InnerException is null ? null : FromException(exception.InnerException));
    }
}
