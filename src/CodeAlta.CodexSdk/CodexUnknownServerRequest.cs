using System.Text.Json;

namespace CodeAlta.CodexSdk;

/// <summary>
/// Represents a server-initiated JSON-RPC request whose method is not modeled by the generated <see cref="ServerRequest"/> schema.
/// </summary>
/// <param name="RequestId">The JSON-RPC request id the client may need to reference when replying.</param>
/// <param name="Method">The raw JSON-RPC method name.</param>
/// <param name="Params">The raw JSON-RPC params payload.</param>
public sealed record CodexUnknownServerRequest(
    RequestId RequestId,
    string Method,
    JsonElement Params);
