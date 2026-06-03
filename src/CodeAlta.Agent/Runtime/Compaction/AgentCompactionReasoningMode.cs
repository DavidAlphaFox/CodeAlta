namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：定义压缩时助手推理内容的包含策略枚举
/// <summary>
/// Controls how compaction includes assistant reasoning content.
/// </summary>
internal enum AgentCompactionReasoningMode
{
    // 压缩输入中完全不包含推理内容
    /// <summary>
    /// Never include reasoning in serialized compaction input.
    /// </summary>
    None,

    // 根据预算和相关性自适应决定是否包含推理内容
    /// <summary>
    /// Include reasoning only when budget and relevance allow it.
    /// </summary>
    Adaptive,

    // 仅包含简短推理摘要而非原始推理片段
    /// <summary>
    /// Include only short reasoning summaries rather than raw reasoning excerpts.
    /// </summary>
    SummaryOnly,
}
