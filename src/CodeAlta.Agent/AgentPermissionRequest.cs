using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

// 模块功能：定义 Agent 权限请求体系，涵盖通用请求、命令执行请求、文件变更请求及权限决策类型
/// <summary>
/// Shared base type for a permission request originating from a provider.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">The event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="InteractionId">Stable interaction identifier.</param>
/// <param name="Kind">The shared permission-request kind.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AgentGenericPermissionRequest), "generic")]
[JsonDerivedType(typeof(AgentCommandPermissionRequest), "command")]
[JsonDerivedType(typeof(AgentFileChangePermissionRequest), "fileChange")]
public abstract record AgentPermissionRequest(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    string InteractionId,
    string Kind)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

// 类型：通用权限请求，适用于未提供结构化提示的 Provider，携带原始 JSON 负载
/// <summary>
/// Generic permission request for providers that do not expose a richer typed prompt.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">The event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="InteractionId">Stable interaction identifier.</param>
/// <param name="Kind">The provider-defined request kind.</param>
/// <param name="Raw">The raw request payload.</param>
public sealed record AgentGenericPermissionRequest(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    string InteractionId,
    string Kind,
    JsonElement Raw)
    : AgentPermissionRequest(ProviderId, SessionId, Timestamp, RunId, InteractionId, Kind);

// 类型：命令执行权限请求，包含命令文本、工作目录、预览动作及策略修订建议
/// <summary>
/// Permission request for command execution.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">The event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="InteractionId">Stable interaction identifier.</param>
/// <param name="ApprovalId">Optional provider-specific approval identifier.</param>
/// <param name="Command">Optional command text.</param>
/// <param name="WorkingDirectory">Optional working directory.</param>
/// <param name="Actions">Optional parsed command actions.</param>
/// <param name="Reason">Optional explanatory reason.</param>
/// <param name="Network">Optional network access request details.</param>
/// <param name="ProposedExecPolicyAmendment">Optional proposed exec policy amendment.</param>
/// <param name="ProposedNetworkPolicyAmendments">Optional proposed network policy amendments.</param>
public sealed record AgentCommandPermissionRequest(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    string InteractionId,
    string? ApprovalId,
    string? Command,
    string? WorkingDirectory,
    IReadOnlyList<AgentCommandPreviewAction>? Actions,
    string? Reason,
    AgentNetworkAccessRequest? Network,
    IReadOnlyList<string>? ProposedExecPolicyAmendment,
    IReadOnlyList<AgentNetworkPolicyAmendment>? ProposedNetworkPolicyAmendments)
    : AgentPermissionRequest(ProviderId, SessionId, Timestamp, RunId, InteractionId, Kind: "commandExecution");

// 类型：文件变更权限请求，包含授权根目录和变更原因
/// <summary>
/// Permission request for file changes.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">The event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="InteractionId">Stable interaction identifier.</param>
/// <param name="GrantRoot">Optional root path to grant for the session.</param>
/// <param name="Reason">Optional explanatory reason.</param>
public sealed record AgentFileChangePermissionRequest(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    string InteractionId,
    string? GrantRoot,
    string? Reason)
    : AgentPermissionRequest(ProviderId, SessionId, Timestamp, RunId, InteractionId, Kind: "fileChange");

// 类型：枚举命令预览动作的类型，用于 UI 展示
/// <summary>
/// Identifies the kind of a parsed command action.
/// </summary>
public enum AgentCommandPreviewKind
{
    /// <summary>
    /// A read-oriented command action.
    /// </summary>
    Read,

    /// <summary>
    /// A list-files command action.
    /// </summary>
    ListFiles,

    /// <summary>
    /// A search command action.
    /// </summary>
    Search,

    /// <summary>
    /// An unknown command action.
    /// </summary>
    Unknown,
}

// 类型：解析后的命令预览动作，供前端展示命令的操作类型、路径及查询
/// <summary>
/// Parsed command action for UI preview.
/// </summary>
/// <param name="Kind">The action kind.</param>
/// <param name="Command">The original command fragment.</param>
/// <param name="Path">Optional filesystem path.</param>
/// <param name="Query">Optional search query.</param>
/// <param name="Name">Optional display name.</param>
public sealed record AgentCommandPreviewAction(
    AgentCommandPreviewKind Kind,
    string Command,
    string? Path = null,
    string? Query = null,
    string? Name = null);

// 类型：网络访问请求详情，包含目标主机和协议
/// <summary>
/// Network access request details.
/// </summary>
/// <param name="Host">The target host.</param>
/// <param name="Protocol">The target protocol.</param>
public sealed record AgentNetworkAccessRequest(
    string Host,
    string Protocol);

// 类型：枚举网络策略修订动作（允许/拒绝）
/// <summary>
/// Network policy amendment action.
/// </summary>
public enum AgentNetworkPolicyAction
{
    /// <summary>
    /// Allow network access.
    /// </summary>
    Allow,

    /// <summary>
    /// Deny network access.
    /// </summary>
    Deny,
}

// 类型：拟议的网络策略修订条目，包含动作和目标主机
/// <summary>
/// Proposed network policy amendment.
/// </summary>
/// <param name="Action">The amendment action.</param>
/// <param name="Host">The target host.</param>
public sealed record AgentNetworkPolicyAmendment(
    AgentNetworkPolicyAction Action,
    string Host);

// 类型：权限请求的决策结果，包含决策类型及可选的策略修订条目
/// <summary>
/// Represents the decision for a permission request.
/// </summary>
/// <param name="Kind">The decision kind.</param>
/// <param name="ExecPolicyAmendment">Optional exec policy amendment.</param>
/// <param name="NetworkPolicyAmendment">Optional network policy amendment.</param>
public sealed record AgentPermissionDecision(
    AgentPermissionDecisionKind Kind,
    IReadOnlyList<string>? ExecPolicyAmendment = null,
    AgentNetworkPolicyAmendment? NetworkPolicyAmendment = null);

// 类型：枚举权限决策的种类（单次允许、会话允许、拒绝、取消）
/// <summary>
/// The kind of permission decision.
/// </summary>
public enum AgentPermissionDecisionKind
{
    /// <summary>
    /// Allow the action once.
    /// </summary>
    AllowOnce,

    /// <summary>
    /// Allow the action for the remainder of the session.
    /// </summary>
    AllowForSession,

    /// <summary>
    /// Deny the action.
    /// </summary>
    Deny,

    /// <summary>
    /// Cancel the request.
    /// </summary>
    Cancel,
}

// 函数功能：权限请求处理委托，接收权限请求并返回异步决策结果
/// <summary>
/// Permission request handler delegate.
/// </summary>
/// <param name="request">The permission request.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
public delegate Task<AgentPermissionDecision> AgentPermissionRequestHandler(
    AgentPermissionRequest request,
    CancellationToken cancellationToken);
