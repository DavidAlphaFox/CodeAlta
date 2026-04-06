namespace CodeAlta.Acp;

/// <summary>
/// Describes how an ACP agent should be installed or invoked locally.
/// </summary>
public sealed class AcpInstallPlan
{
    /// <summary>
    /// Gets or sets the registry manifest.
    /// </summary>
    public required AcpRegistryAgentManifest Manifest { get; init; }

    /// <summary>
    /// Gets or sets the distribution kind.
    /// </summary>
    public required AcpInstallKind Kind { get; init; }

    /// <summary>
    /// Gets or sets the command to run.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Gets or sets the launch arguments.
    /// </summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    /// <summary>
    /// Gets or sets the environment variables.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>
    /// Gets or sets the archive URL for binary installs.
    /// </summary>
    public Uri? ArchiveUri { get; init; }

    /// <summary>
    /// Gets or sets the current platform target identifier.
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>
    /// Gets or sets the relative command path inside the extracted archive.
    /// </summary>
    public string? RelativeCommandPath { get; init; }

    /// <summary>
    /// Gets or sets the package spec for NPX or UVX plans.
    /// </summary>
    public string? Package { get; init; }
}

/// <summary>
/// Identifies the selected ACP install mode.
/// </summary>
public enum AcpInstallKind
{
    /// <summary>
    /// A platform archive download and extraction.
    /// </summary>
    Binary,

    /// <summary>
    /// An NPX package launch.
    /// </summary>
    Npx,

    /// <summary>
    /// A UVX package launch.
    /// </summary>
    Uvx,
}

/// <summary>
/// Describes a resolved ACP installation that CodeAlta can start directly.
/// </summary>
public sealed class AcpResolvedInstall
{
    /// <summary>
    /// Gets or sets the registry manifest.
    /// </summary>
    public required AcpRegistryAgentManifest Manifest { get; init; }

    /// <summary>
    /// Gets or sets the selected install kind.
    /// </summary>
    public required AcpInstallKind Kind { get; init; }

    /// <summary>
    /// Gets or sets the absolute command path or package runner.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Gets or sets the launch arguments.
    /// </summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    /// <summary>
    /// Gets or sets launch environment variables.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>
    /// Gets or sets the working directory to use when launching the agent.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets the install root for binary distributions.
    /// </summary>
    public string? InstallRoot { get; init; }
}
