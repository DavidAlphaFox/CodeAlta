using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

// 模块功能：描述已存储或活跃中的 CodeAlta Agent 会话元数据及各运行时特定详情类型
/// <summary>
/// Describes a stored or active CodeAlta agent session.
/// </summary>
/// <param name="SessionId">The durable session identifier.</param>
/// <param name="CreatedAt">The time the session was created.</param>
/// <param name="UpdatedAt">The time the session was last updated.</param>
/// <param name="Summary">Optional session summary or preview text.</param>
/// <param name="Context">Optional directory/repo context.</param>
/// <param name="WorkspacePath">Optional workspace path for the session.</param>
/// <param name="Details">Optional provider or runtime-specific metadata details.</param>
/// <param name="ProtocolFamily">Optional last-used protocol family.</param>
/// <param name="ProviderKey">Optional last-used configured provider key.</param>
/// <param name="ModelId">Optional last-used model identifier.</param>
/// <param name="ParentSessionId">Optional parent session identifier used only for lineage/orchestration metadata.</param>
/// <param name="CreatedBySessionId">Optional session identifier that created this session.</param>
/// <param name="CreatedByRunId">Optional run identifier that created this session.</param>
public sealed record AgentSessionMetadata(
    string SessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Summary = null,
    AgentSessionContext? Context = null,
    string? WorkspacePath = null,
    AgentSessionMetadataDetails? Details = null,
    string? ProtocolFamily = null,
    string? ProviderKey = null,
    string? ModelId = null,
    string? ParentSessionId = null,
    string? CreatedBySessionId = null,
    AgentRunId? CreatedByRunId = null);

// 类型：Provider/Runtime 特定会话元数据详情基类，通过 $type 区分 Codex、Copilot 等子类型
/// <summary>
/// Base type for provider/runtime-specific session metadata details.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CodexSessionMetadataDetails), "codex")]
[JsonDerivedType(typeof(CopilotSessionMetadataDetails), "copilot")]
[JsonDerivedType(typeof(RawApiSessionMetadataDetails), "rawApi")]
public abstract record AgentSessionMetadataDetails;

// 类型：Codex 特定会话元数据，包含提供者标识、来源、状态、是否临时会话等信息
/// <summary>
/// Codex-specific session metadata details.
/// </summary>
/// <param name="ModelProvider">The model provider reported by Codex when available.</param>
/// <param name="Source">The provider-reported origin for the session.</param>
/// <param name="Status">The provider-reported runtime status.</param>
/// <param name="IsEphemeral">Whether the provider-reported session is ephemeral.</param>
/// <param name="SessionName">The optional legacy provider title reported by Codex.</param>
public sealed record CodexSessionMetadataDetails(
    string? ModelProvider = null,
    string? Source = null,
    string? Status = null,
    bool IsEphemeral = false,
    string? SessionName = null)
    : AgentSessionMetadataDetails;

// 类型：Copilot 特定会话元数据，标记会话是否在远端运行
/// <summary>
/// Copilot-specific session metadata details.
/// </summary>
/// <param name="IsRemote">Whether the session is running remotely.</param>
public sealed record CopilotSessionMetadataDetails(
    bool IsRemote = false)
    : AgentSessionMetadataDetails;

// 类型：原始 API 运行时特定会话元数据，记录提供者显示名、基础 URI、提供者会话 ID 及本地标题
/// <summary>
/// Provider-backed agent-runtime session metadata details.
/// </summary>
/// <param name="ProviderDisplayName">The configured provider display name.</param>
/// <param name="ProviderBaseUri">The configured provider base URI when applicable.</param>
/// <param name="ProviderSessionId">The provider-native session or response identifier when available.</param>
/// <param name="Title">The latest locally persisted session title when available.</param>
public sealed record RawApiSessionMetadataDetails(
    string? ProviderDisplayName = null,
    string? ProviderBaseUri = null,
    string? ProviderSessionId = null,
    string? Title = null)
    : AgentSessionMetadataDetails;

