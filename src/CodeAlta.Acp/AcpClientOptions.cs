namespace CodeAlta.Acp;

/// <summary>
/// Options used to create and initialize an ACP client connection.
/// </summary>
public sealed class AcpClientOptions
{
    /// <summary>
    /// Gets or sets the process options used to launch the agent.
    /// </summary>
    public required AcpProcessOptions ProcessOptions { get; init; }

    /// <summary>
    /// Gets or sets the client implementation info sent during initialization.
    /// </summary>
    public required Implementation ClientInfo { get; init; }

    /// <summary>
    /// Gets or sets the advertised client capabilities.
    /// </summary>
    public required ClientCapabilities ClientCapabilities { get; init; }

    /// <summary>
    /// Gets or sets the protocol version to negotiate.
    /// </summary>
    public ushort ProtocolVersion { get; init; } = 1;
}
