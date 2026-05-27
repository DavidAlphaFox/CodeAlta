using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

/// <summary>
/// Shared base type for a permission request originating from a backend.
/// </summary>
/// <param name="BackendId">The backend identifier.</param>
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
    AgentBackendId BackendId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    string InteractionId,
    string Kind)
    : AgentEvent(BackendId, SessionId, Timestamp, RunId);

/// <summary>
/// Generic permission request for backends that do not expose a richer typed prompt.
/// </summary>
/// <param name="BackendId">The backend identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">The event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="InteractionId">Stable interaction identifier.</param>
/// <param name="Kind">The backend-defined request kind.</param>
/// <param name="Raw">The raw request payload.</param>
public sealed record AgentGenericPermissionRequest(
    AgentBackendId BackendId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    string InteractionId,
    string Kind,
    JsonElement Raw)
    : AgentPermissionRequest(BackendId, SessionId, Timestamp, RunId, InteractionId, Kind);

/// <summary>
/// Permission request for command execution.
/// </summary>
/// <param name="BackendId">The backend identifier.</param>
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
    AgentBackendId BackendId,
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
    : AgentPermissionRequest(BackendId, SessionId, Timestamp, RunId, InteractionId, Kind: "commandExecution");

/// <summary>
/// Permission request for file changes.
/// </summary>
/// <param name="BackendId">The backend identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">The event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="InteractionId">Stable interaction identifier.</param>
/// <param name="GrantRoot">Optional root path to grant for the session.</param>
/// <param name="Reason">Optional explanatory reason.</param>
public sealed record AgentFileChangePermissionRequest(
    AgentBackendId BackendId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    string InteractionId,
    string? GrantRoot,
    string? Reason)
    : AgentPermissionRequest(BackendId, SessionId, Timestamp, RunId, InteractionId, Kind: "fileChange");

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

/// <summary>
/// Network access request details.
/// </summary>
/// <param name="Host">The target host.</param>
/// <param name="Protocol">The target protocol.</param>
public sealed record AgentNetworkAccessRequest(
    string Host,
    string Protocol);

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

/// <summary>
/// Proposed network policy amendment.
/// </summary>
/// <param name="Action">The amendment action.</param>
/// <param name="Host">The target host.</param>
public sealed record AgentNetworkPolicyAmendment(
    AgentNetworkPolicyAction Action,
    string Host);

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

/// <summary>
/// Permission request handler delegate.
/// </summary>
/// <param name="request">The permission request.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
public delegate Task<AgentPermissionDecision> AgentPermissionRequestHandler(
    AgentPermissionRequest request,
    CancellationToken cancellationToken);
