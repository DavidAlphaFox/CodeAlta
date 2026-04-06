using CodeAlta.Acp;

namespace CodeAlta.Agent.Acp;

/// <summary>
/// Options for an ACP-backed <see cref="IAgentBackend"/>.
/// </summary>
public sealed class AcpAgentBackendOptions
{
    /// <summary>
    /// Gets or sets the ACP agent identifier.
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Gets or sets the display name shown in the UI.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets or sets the process options used to start the ACP agent.
    /// </summary>
    public required AcpProcessOptions ProcessOptions { get; init; }

    /// <summary>
    /// Gets or sets the optional ACP registry identifier.
    /// </summary>
    public string? RegistryId { get; init; }

    /// <summary>
    /// Gets or sets the preferred ACP authentication method identifier.
    /// </summary>
    public string? AuthenticationMethodId { get; init; }

    /// <summary>
    /// Gets or sets the state root used for journals and runtime files.
    /// </summary>
    public string? StateRootPath { get; init; }

    /// <summary>
    /// Gets or sets whether filesystem client capabilities are enabled.
    /// </summary>
    public bool EnableFilesystem { get; init; } = true;

    /// <summary>
    /// Gets or sets whether terminal client capabilities are enabled.
    /// </summary>
    public bool EnableTerminal { get; init; } = true;

    /// <summary>
    /// Gets or sets whether unstable elicitation may be advertised.
    /// </summary>
    public bool EnableElicitation { get; init; }

    /// <summary>
    /// Gets or sets whether unstable ACP features are allowed in principle.
    /// </summary>
    public bool UseUnstableFeatures { get; init; } = true;

    /// <summary>
    /// Gets or sets fine-grained unstable feature flags.
    /// </summary>
    public AcpUnstableFeatureOptions UnstableFeatures { get; init; } = new();

    internal Func<CancellationToken, Task<AcpClient>>? ClientFactory { get; init; }
}
