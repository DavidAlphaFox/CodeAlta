using System.Text.Json.Serialization;

namespace CodeAlta.Catalog;

/// <summary>
/// Describes a configured ACP agent backend.
/// </summary>
public sealed class AcpBackendDefinition
{
    /// <summary>
    /// Gets or sets the ACP agent identifier.
    /// </summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user-facing display name.
    /// </summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets whether the backend is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the ACP registry identifier when known.
    /// </summary>
    [JsonPropertyName("registry_id")]
    public string? RegistryId { get; set; }

    /// <summary>
    /// Gets or sets the command to start the ACP agent.
    /// </summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>
    /// Gets or sets the command-line arguments.
    /// </summary>
    [JsonPropertyName("args")]
    public List<string>? Arguments { get; set; }

    /// <summary>
    /// Gets or sets the working directory used to start the agent.
    /// </summary>
    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets additional environment variables.
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Gets or sets whether unstable ACP features may be used.
    /// </summary>
    [JsonPropertyName("use_unstable")]
    public bool UseUnstable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether terminal client capabilities should be exposed.
    /// </summary>
    [JsonPropertyName("enable_terminal")]
    public bool EnableTerminal { get; set; } = true;

    /// <summary>
    /// Gets or sets whether filesystem client capabilities should be exposed.
    /// </summary>
    [JsonPropertyName("enable_filesystem")]
    public bool EnableFilesystem { get; set; } = true;

    /// <summary>
    /// Gets or sets whether unstable elicitation should be exposed when supported.
    /// </summary>
    [JsonPropertyName("enable_elicitation")]
    public bool EnableElicitation { get; set; }
}
