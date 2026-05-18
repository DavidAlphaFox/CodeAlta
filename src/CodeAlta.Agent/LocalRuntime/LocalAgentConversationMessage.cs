using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAlta.Agent.LocalRuntime;

/// <summary>
/// Represents a replayable local-runtime conversation message.
/// </summary>
/// <param name="Role">The conversation role.</param>
/// <param name="Parts">The message parts.</param>
public sealed record LocalAgentConversationMessage(
    LocalAgentConversationRole Role,
    IReadOnlyList<LocalAgentMessagePart> Parts);

/// <summary>
/// Identifies a replayable local-runtime conversation role.
/// </summary>
public enum LocalAgentConversationRole
{
    /// <summary>
    /// A system message.
    /// </summary>
    System,

    /// <summary>
    /// A user-authored message.
    /// </summary>
    User,

    /// <summary>
    /// An assistant-authored message.
    /// </summary>
    Assistant,

    /// <summary>
    /// A tool-result message.
    /// </summary>
    Tool,
}

/// <summary>
/// Base type for replayable local-runtime message parts.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(LocalAgentMessagePart.Text), "text")]
[JsonDerivedType(typeof(LocalAgentMessagePart.Reasoning), "reasoning")]
[JsonDerivedType(typeof(LocalAgentMessagePart.ToolCall), "toolCall")]
[JsonDerivedType(typeof(LocalAgentMessagePart.ToolResult), "toolResult")]
[JsonDerivedType(typeof(LocalAgentMessagePart.Uri), "uri")]
[JsonDerivedType(typeof(LocalAgentMessagePart.Data), "data")]
public abstract record LocalAgentMessagePart
{
    /// <summary>
    /// A plain text message part.
    /// </summary>
    /// <param name="Value">The text value.</param>
    public sealed record Text(string Value) : LocalAgentMessagePart;

    /// <summary>
    /// A reasoning message part.
    /// </summary>
    /// <param name="Value">The optional visible reasoning text.</param>
    /// <param name="ProtectedData">Optional provider-protected reasoning payload.</param>
    /// <param name="Provenance">Optional provider/model identity that produced the reasoning payload.</param>
    public sealed record Reasoning(
        string? Value,
        string? ProtectedData = null,
        LocalAgentReasoningProvenance? Provenance = null) : LocalAgentMessagePart;

    /// <summary>
    /// A tool-call message part.
    /// </summary>
    /// <param name="CallId">The stable tool call identifier.</param>
    /// <param name="Name">The tool name.</param>
    /// <param name="Arguments">The tool arguments.</param>
    public sealed record ToolCall(string CallId, string Name, JsonElement Arguments) : LocalAgentMessagePart;

    /// <summary>
    /// A tool-result message part.
    /// </summary>
    /// <param name="CallId">The stable tool call identifier.</param>
    /// <param name="Result">The structured tool result.</param>
    public sealed record ToolResult(string CallId, AgentToolResult Result) : LocalAgentMessagePart;

    /// <summary>
    /// A URI-backed content part.
    /// </summary>
    /// <param name="Value">The URI value.</param>
    /// <param name="MediaType">The optional media type.</param>
    /// <param name="Name">The optional display name.</param>
    public sealed record Uri(string Value, string? MediaType = null, string? Name = null) : LocalAgentMessagePart;

    /// <summary>
    /// An inline data content part.
    /// </summary>
    /// <param name="Base64Data">The base64-encoded payload.</param>
    /// <param name="MediaType">The media type.</param>
    /// <param name="Name">The optional display name.</param>
    public sealed record Data(string Base64Data, string MediaType, string? Name = null) : LocalAgentMessagePart;
}