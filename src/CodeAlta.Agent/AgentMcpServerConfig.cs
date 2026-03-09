using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

/// <summary>
/// Describes an MCP server made available to an agent session.
/// </summary>
[JsonDerivedType(typeof(AgentLocalMcpServerConfig), typeDiscriminator: "local")]
[JsonDerivedType(typeof(AgentRemoteMcpServerConfig), typeDiscriminator: "remote")]
public abstract record AgentMcpServerConfig
{
    /// <summary>
    /// Gets the tools exposed from this server. When <see langword="null"/>, all tools are allowed.
    /// </summary>
    public IReadOnlyList<string>? EnabledTools { get; init; }

    /// <summary>
    /// Gets the default timeout to apply to tool calls for this server.
    /// </summary>
    public TimeSpan? ToolTimeout { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether session startup should fail if this server cannot be initialized.
    /// </summary>
    public bool Required { get; init; }
}

/// <summary>
/// Describes an MCP server started locally via stdio.
/// </summary>
/// <param name="Command">The command used to start the MCP server.</param>
public sealed record AgentLocalMcpServerConfig(string Command) : AgentMcpServerConfig
{
    /// <summary>
    /// Gets the command-line arguments passed to the server process.
    /// </summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    /// <summary>
    /// Gets the environment variables passed to the server process.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>
    /// Gets the working directory for the server process.
    /// </summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// Selects the remote MCP transport.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgentMcpRemoteTransport>))]
public enum AgentMcpRemoteTransport
{
    /// <summary>
    /// Streamable HTTP transport.
    /// </summary>
    Http,

    /// <summary>
    /// Server-sent events transport.
    /// </summary>
    Sse
}

/// <summary>
/// Describes an MCP server reached over the network.
/// </summary>
/// <param name="Url">The remote MCP server URL.</param>
public sealed record AgentRemoteMcpServerConfig(string Url) : AgentMcpServerConfig
{
    /// <summary>
    /// Gets the remote transport kind.
    /// </summary>
    public AgentMcpRemoteTransport Transport { get; init; } = AgentMcpRemoteTransport.Http;

    /// <summary>
    /// Gets the static HTTP headers included with requests.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Gets the environment variable name that contains the bearer token to use for the server.
    /// </summary>
    public string? BearerTokenEnvironmentVariable { get; init; }

    /// <summary>
    /// Gets HTTP headers whose values should be read from environment variables.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentHeaders { get; init; }
}
