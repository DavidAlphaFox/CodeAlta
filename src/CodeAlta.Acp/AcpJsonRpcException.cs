using System.Text.Json;

namespace CodeAlta.Acp;

/// <summary>
/// Represents a JSON-RPC error returned by an ACP agent.
/// </summary>
public sealed class AcpJsonRpcException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AcpJsonRpcException"/> class.
    /// </summary>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="message">The JSON-RPC error message.</param>
    /// <param name="errorPayload">Optional raw error payload.</param>
    public AcpJsonRpcException(int code, string message, JsonElement? errorPayload = null)
        : base(message)
    {
        Code = code;
        ErrorPayload = errorPayload;
    }

    /// <summary>
    /// Gets the JSON-RPC error code.
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// Gets the optional raw error payload.
    /// </summary>
    public JsonElement? ErrorPayload { get; }
}
