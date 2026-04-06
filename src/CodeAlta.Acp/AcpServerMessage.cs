using System.Text.Json;

namespace CodeAlta.Acp;

/// <summary>
/// Represents a raw server-initiated ACP message.
/// </summary>
/// <param name="Method">The JSON-RPC method name.</param>
/// <param name="Params">The raw params payload.</param>
/// <param name="RequestId">The request id when the message expects a response; otherwise <see langword="null"/>.</param>
public sealed record AcpServerMessage(
    string Method,
    JsonElement Params,
    RequestId? RequestId);
