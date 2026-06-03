namespace CodeAlta.Agent.Runtime;

// 模块功能：表示存储在规范事件日志中的本地压缩快照记录
/// <summary>
/// Represents a persisted local compaction snapshot stored in the canonical event log.
/// </summary>
public sealed record AgentCompactionSnapshot
{
    // 说明：快照所涵盖的规范事件数量
    /// <summary>
    /// Gets or initializes the canonical event count included by the snapshot.
    /// </summary>
    public required int IncludedEventCount { get; init; }

    // 说明：被快照摘要的对话消息数量
    /// <summary>
    /// Gets or initializes the number of conversation messages summarized by the snapshot.
    /// </summary>
    public required int SummarizedMessageCount { get; init; }

    // 说明：代表压缩历史的合成回放消息
    /// <summary>
    /// Gets or initializes the synthetic replay message that represents the compacted history.
    /// </summary>
    public required AgentConversationMessage SummaryMessage { get; init; }
}
