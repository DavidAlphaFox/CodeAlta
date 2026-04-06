namespace CodeAlta.Agent.Acp;

/// <summary>
/// Controls optional ACP unstable feature usage.
/// </summary>
public sealed class AcpUnstableFeatureOptions
{
    /// <summary>
    /// Gets or sets whether unstable `session/resume` may be used when stable load is unavailable.
    /// </summary>
    public bool UseSessionResume { get; set; } = true;

    /// <summary>
    /// Gets or sets whether unstable `session/close` may be used during disposal.
    /// </summary>
    public bool UseSessionClose { get; set; } = true;

    /// <summary>
    /// Gets or sets whether unstable `session/delete` may be used.
    /// </summary>
    public bool UseSessionDelete { get; set; } = true;

    /// <summary>
    /// Gets or sets whether unstable `session/elicitation` may be used.
    /// </summary>
    public bool UseElicitation { get; set; }

    /// <summary>
    /// Gets or sets whether unstable `session/set_model` may be used.
    /// </summary>
    public bool UseSetModel { get; set; } = true;
}
