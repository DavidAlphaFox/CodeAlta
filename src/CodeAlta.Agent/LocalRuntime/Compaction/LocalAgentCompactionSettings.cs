namespace CodeAlta.Agent.LocalRuntime.Compaction;

/// <summary>
/// Normalized local-runtime compaction settings.
/// </summary>
/// <param name="Enabled">Whether automatic threshold compaction is enabled.</param>
/// <param name="Ratio">The active-context/input-limit ratio that triggers automatic compaction.</param>
/// <param name="KeepLastUserMessage">Whether the latest user message should be preserved as an anchor.</param>
/// <param name="AllowSplitTurn">Whether compaction may split a large turn while preserving continuation state.</param>
public sealed record LocalAgentCompactionSettings(
    bool Enabled,
    double Ratio,
    bool KeepLastUserMessage,
    bool AllowSplitTurn)
{
    /// <summary>
    /// The default enabled value.
    /// </summary>
    public const bool DefaultEnabled = true;

    /// <summary>
    /// The default compaction trigger ratio.
    /// </summary>
    public const double DefaultRatio = 0.95;

    /// <summary>
    /// The default keep-last-user-message value.
    /// </summary>
    public const bool DefaultKeepLastUserMessage = true;

    /// <summary>
    /// The default allow-split-turn value.
    /// </summary>
    public const bool DefaultAllowSplitTurn = true;

    /// <summary>
    /// Gets the runtime defaults.
    /// </summary>
    public static LocalAgentCompactionSettings Default { get; } = new(
        Enabled: DefaultEnabled,
        Ratio: DefaultRatio,
        KeepLastUserMessage: DefaultKeepLastUserMessage,
        AllowSplitTurn: DefaultAllowSplitTurn);
}
