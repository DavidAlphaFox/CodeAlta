using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAlta.Acp;

/// <summary>
/// Represents the ACP registry document.
/// </summary>
public sealed class AcpRegistryDocument
{
    /// <summary>
    /// Gets or sets the registry schema version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the published ACP agents.
    /// </summary>
    [JsonPropertyName("agents")]
    public List<AcpRegistryAgentManifest> Agents { get; set; } = [];

    /// <summary>
    /// Gets or sets optional registry extensions payload.
    /// </summary>
    [JsonPropertyName("extensions")]
    public JsonElement? Extensions { get; set; }
}

/// <summary>
/// Represents one ACP agent entry from the registry.
/// </summary>
public sealed class AcpRegistryAgentManifest
{
    /// <summary>
    /// Gets or sets the stable registry identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the published agent version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the short description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source repository URL.
    /// </summary>
    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    /// <summary>
    /// Gets or sets the project website URL.
    /// </summary>
    [JsonPropertyName("website")]
    public string? Website { get; set; }

    /// <summary>
    /// Gets or sets the author or organization labels.
    /// </summary>
    [JsonPropertyName("authors")]
    public List<string>? Authors { get; set; }

    /// <summary>
    /// Gets or sets the declared license.
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// Gets or sets the icon URL.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the install distributions.
    /// </summary>
    [JsonPropertyName("distribution")]
    public AcpRegistryDistribution Distribution { get; set; } = new();
}

/// <summary>
/// Represents the distribution section of an ACP registry manifest.
/// </summary>
public sealed class AcpRegistryDistribution
{
    /// <summary>
    /// Gets or sets the binary distribution definitions keyed by target identifier.
    /// </summary>
    [JsonPropertyName("binary")]
    public Dictionary<string, AcpRegistryBinaryPackage>? Binary { get; set; }

    /// <summary>
    /// Gets or sets the optional NPX distribution.
    /// </summary>
    [JsonPropertyName("npx")]
    public AcpRegistryPackageDistribution? Npx { get; set; }

    /// <summary>
    /// Gets or sets the optional UVX distribution.
    /// </summary>
    [JsonPropertyName("uvx")]
    public AcpRegistryPackageDistribution? Uvx { get; set; }
}

/// <summary>
/// Represents one binary package entry from the ACP registry.
/// </summary>
public sealed class AcpRegistryBinaryPackage
{
    /// <summary>
    /// Gets or sets the archive URL.
    /// </summary>
    [JsonPropertyName("archive")]
    public string Archive { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the relative command to launch after extraction.
    /// </summary>
    [JsonPropertyName("cmd")]
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets command-line arguments.
    /// </summary>
    [JsonPropertyName("args")]
    public List<string>? Arguments { get; set; }

    /// <summary>
    /// Gets or sets environment variables.
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string>? EnvironmentVariables { get; set; }
}

/// <summary>
/// Represents a package-manager-based distribution entry from the ACP registry.
/// </summary>
public sealed class AcpRegistryPackageDistribution
{
    /// <summary>
    /// Gets or sets the package specifier.
    /// </summary>
    [JsonPropertyName("package")]
    public string Package { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional command-line arguments.
    /// </summary>
    [JsonPropertyName("args")]
    public List<string>? Arguments { get; set; }

    /// <summary>
    /// Gets or sets environment variables.
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string>? EnvironmentVariables { get; set; }
}
