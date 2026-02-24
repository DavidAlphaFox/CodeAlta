using System.Text.Json;

namespace CodeNoesis.CodexSdk;

/// <summary>
/// Represents a JSON-RPC error returned by the codex app-server.
/// </summary>
public sealed class JsonRpcException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcException"/> class.
    /// </summary>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="data">Optional raw JSON error data.</param>
    public JsonRpcException(int code, string message, JsonElement? data = null)
        : base(message)
    {
        Code = code;
        Data = data;
    }

    /// <summary>
    /// Gets the JSON-RPC error code.
    /// </summary>
    /// <remarks>
    /// Common codes:
    /// <list type="bullet">
    /// <item><description><c>-32001</c>: Server overloaded (retryable).</description></item>
    /// <item><description><c>-32600</c>: Invalid request.</description></item>
    /// <item><description><c>-32601</c>: Method not found.</description></item>
    /// <item><description><c>-32602</c>: Invalid params.</description></item>
    /// <item><description><c>-32603</c>: Internal error.</description></item>
    /// </list>
    /// </remarks>
    public int Code { get; }

    /// <summary>
    /// Gets the optional raw JSON error data payload from the server.
    /// </summary>
    public new JsonElement? Data { get; }

    /// <summary>
    /// Gets a value indicating whether this error is retryable (server overloaded).
    /// </summary>
    public bool IsRetryable => Code == -32001;
}
