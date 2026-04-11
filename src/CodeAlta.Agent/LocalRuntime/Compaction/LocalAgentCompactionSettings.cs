namespace CodeAlta.Agent.LocalRuntime.Compaction;

/// <summary>
/// Normalized local-runtime compaction settings.
/// </summary>
public sealed record LocalAgentCompactionSettings(
    bool Enabled,
    double TriggerThreshold,
    double TargetThreshold,
    int ReservedOutputTokens,
    int ReservedOverheadTokens,
    bool KeepLastUserMessage,
    bool AllowSplitTurn)
{
    /// <summary>
    /// Gets or initializes the ideal post-compaction context ratio.
    /// </summary>
    public double TargetContextRatioIdeal { get; init; } = 0.03;

    /// <summary>
    /// Gets or initializes the preferred maximum post-compaction context ratio.
    /// </summary>
    public double TargetContextRatioMax { get; init; } = 0.10;

    /// <summary>
    /// Gets or initializes the preferred token budget for the retained recent suffix.
    /// </summary>
    public int RecentSuffixTargetTokens { get; init; } = 20_000;

    /// <summary>
    /// Gets or initializes the maximum output tokens allowed for the summarizer call.
    /// </summary>
    public int SummaryOutputTokens { get; init; } = 1_024;

    /// <summary>
    /// Gets or initializes the maximum input tokens allowed for a single summarizer request.
    /// </summary>
    public int SummaryInputTokens { get; init; } = 24_000;

    /// <summary>
    /// Gets or initializes the per-item tool-result excerpt cap.
    /// </summary>
    public int ToolResultCharsPerItem { get; init; } = 1_200;

    /// <summary>
    /// Gets or initializes the total tool-result excerpt cap.
    /// </summary>
    public int ToolResultCharsTotal { get; init; } = 6_000;

    /// <summary>
    /// Gets or initializes the per-item reasoning excerpt cap.
    /// </summary>
    public int ReasoningCharsPerItem { get; init; } = 600;

    /// <summary>
    /// Gets or initializes the total reasoning excerpt cap.
    /// </summary>
    public int ReasoningCharsTotal { get; init; } = 3_000;

    /// <summary>
    /// Gets or initializes how reasoning should be retained during compaction.
    /// </summary>
    public LocalAgentCompactionReasoningMode ReasoningMode { get; init; } = LocalAgentCompactionReasoningMode.Adaptive;

    /// <summary>
    /// Gets or initializes the maximum recursive chunking passes.
    /// </summary>
    public int MaxChunkPasses { get; init; } = 4;

    /// <summary>
    /// Gets or initializes whether the latest oversized anchor may be reduced.
    /// </summary>
    public bool AllowOversizedAnchorReduction { get; init; } = true;

    /// <summary>
    /// Gets or initializes whether recency should be favored when trimming messages.
    /// </summary>
    public bool PreferRecentMessages { get; init; } = true;

    /// <summary>
    /// Gets or initializes whether recent tool outputs should be favored when trimming excerpts.
    /// </summary>
    public bool PreferRecentToolOutputs { get; init; } = true;

    /// <summary>
    /// Gets or initializes whether message dropping is allowed only after the summary-input budget is exceeded.
    /// </summary>
    public bool DropMessagesOnlyWhenSummaryInputExceedsBudget { get; init; } = true;

    /// <summary>
    /// Gets the runtime defaults.
    /// </summary>
    public static LocalAgentCompactionSettings Default { get; } = new(
        Enabled: true,
        TriggerThreshold: 0.85,
        TargetThreshold: 0.50,
        ReservedOutputTokens: 4096,
        ReservedOverheadTokens: 2048,
        KeepLastUserMessage: true,
        AllowSplitTurn: true)
    {
        TargetContextRatioIdeal = 0.03,
        TargetContextRatioMax = 0.10,
        RecentSuffixTargetTokens = 20_000,
        SummaryOutputTokens = 1_024,
        SummaryInputTokens = 24_000,
        ToolResultCharsPerItem = 1_200,
        ToolResultCharsTotal = 6_000,
        ReasoningCharsPerItem = 600,
        ReasoningCharsTotal = 3_000,
        ReasoningMode = LocalAgentCompactionReasoningMode.Adaptive,
        MaxChunkPasses = 4,
        AllowOversizedAnchorReduction = true,
        PreferRecentMessages = true,
        PreferRecentToolOutputs = true,
        DropMessagesOnlyWhenSummaryInputExceedsBudget = true,
    };
}
