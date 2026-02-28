using System.Text.Json.Serialization;

namespace CodeAlta.CodexSdk;

/// <summary>
/// Emitted when a pending server-initiated request (e.g. approval or request_user_input) is resolved or cleared.
/// </summary>
/// <remarks>
/// This notification is documented in the codex app-server README as <c>serverRequest/resolved</c>.
/// Some schema bundles may omit this type; CodeAlta defines it manually so clients can handle it.
/// </remarks>
public sealed partial record ServerRequestResolvedNotification
{
    /// <summary>The thread id associated with the request.</summary>
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>The JSON-RPC id of the server request that was resolved.</summary>
    [JsonPropertyName("requestId")]
    public long RequestId { get; set; }
}

