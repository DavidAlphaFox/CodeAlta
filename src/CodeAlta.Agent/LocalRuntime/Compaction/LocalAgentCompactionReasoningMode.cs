namespace CodeAlta.Agent.LocalRuntime.Compaction;

/// <summary>
/// Controls how compaction includes assistant reasoning content.
/// </summary>
internal enum LocalAgentCompactionReasoningMode
{
    /// <summary>
    /// Never include reasoning in serialized compaction input.
    /// </summary>
    None,

    /// <summary>
    /// Include reasoning only when budget and relevance allow it.
    /// </summary>
    Adaptive,

    /// <summary>
    /// Include only short reasoning summaries rather than raw reasoning excerpts.
    /// </summary>
    SummaryOnly,
}
