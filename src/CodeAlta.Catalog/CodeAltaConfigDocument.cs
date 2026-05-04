using System.Text.Json.Serialization;

namespace CodeAlta.Catalog;

/// <summary>
/// Represents the top-level CodeAlta TOML configuration document.
/// </summary>
public sealed class CodeAltaConfigDocument
{
    /// <summary>
    /// Gets or sets chat-level defaults and preferences.
    /// </summary>
    [JsonPropertyName("chat")]
    public CodeAltaChatSettingsDocument? Chat { get; set; }

    /// <summary>
    /// Gets or sets ACP backend definitions keyed by agent id.
    /// </summary>
    [JsonPropertyName("acp")]
    public CodeAltaAcpSettingsDocument? Acp { get; set; }

    /// <summary>
    /// Gets or sets configured provider definitions keyed by provider key.
    /// </summary>
    [JsonPropertyName("providers")]
    public Dictionary<string, CodeAltaProviderDocument>? Providers { get; set; }

    /// <summary>
    /// Gets or sets plugin configuration keyed by plugin package id or built-in plugin id.
    /// </summary>
    [JsonPropertyName("plugins")]
    public Dictionary<string, CodeAltaPluginSettingsDocument>? Plugins { get; set; }
}

/// <summary>
/// Represents plugin-specific configuration settings.
/// </summary>
public sealed class CodeAltaPluginSettingsDocument
{
    /// <summary>
    /// Gets or sets a value indicating whether the plugin is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

/// <summary>
/// Represents chat-specific configuration settings.
/// </summary>
public sealed class CodeAltaChatSettingsDocument
{
    /// <summary>
    /// Gets or sets the default provider key.
    /// </summary>
    [JsonPropertyName("default_provider")]
    public string? DefaultProvider { get; set; }
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
    public Dictionary<string, AcpBackendDefinition>? Agents { get; set; }
}
