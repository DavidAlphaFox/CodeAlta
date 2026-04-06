using System.Text.Json.Serialization;

namespace CodeAlta.Catalog;

/// <summary>
/// Represents the top-level CodeAlta TOML configuration document.
/// </summary>
public sealed class CodeAltaConfigDocument
{
    /// <summary>
    /// Gets or sets per-backend defaults and overrides.
    /// </summary>
    [JsonPropertyName("backends")]
    public Dictionary<string, CodeAltaBackendSettingsDocument> Backends { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets ACP backend definitions keyed by agent id.
    /// </summary>
    [JsonPropertyName("acp")]
    public CodeAltaAcpSettingsDocument Acp { get; set; } = new();
}

/// <summary>
/// Represents the TOML settings for a single backend.
/// </summary>
public sealed class CodeAltaBackendSettingsDocument
{
    /// <summary>
    /// Gets or sets the preferred model identifier.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the preferred reasoning effort.
    /// </summary>
    [JsonPropertyName("reasoning_effort")]
    public string? ReasoningEffort { get; set; }
}

/// <summary>
/// Represents ACP-specific configuration settings.
/// </summary>
public sealed class CodeAltaAcpSettingsDocument
{
    /// <summary>
    /// Gets or sets configured ACP agent backends keyed by agent id.
    /// </summary>
    [JsonPropertyName("agents")]
    public Dictionary<string, AcpBackendDefinition> Agents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
