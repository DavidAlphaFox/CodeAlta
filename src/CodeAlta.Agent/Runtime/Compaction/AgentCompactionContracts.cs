namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：定义 Agent 压缩流程中使用的触发类型、Token 预算、准备/结果/请求/响应及统计等内部数据契约
// 类型：压缩触发方式
internal enum AgentCompactionTrigger
{
    Manual,    // 手动触发
    Threshold, // 达到阈值自动触发
    Overflow,  // 上下文溢出触发
}

// 类型：Token 预算，包含总上下文窗口、输入上下文上限和最大输出 Token 数
internal sealed record AgentTokenBudget(
    long? TotalContextEnvelope,
    long? InputContextLimit,
    long? MaxOutputTokens);

// 类型：Token 估算结果，包含 Token 数量、估算来源及是否为预估值
internal sealed record AgentTokenEstimate(
    long Tokens,
    string Source,
    bool IsEstimated);

// 类型：压缩准备数据，描述待摘要消息、保留消息、锚点及压缩前 Token 估算等上下文
internal sealed record AgentCompactionPreparation(
    AgentCompactionTrigger Trigger,
    IReadOnlyList<AgentConversationMessage> MessagesToSummarize,
    IReadOnlyList<AgentConversationMessage> TurnPrefixMessages,
    IReadOnlyList<AgentConversationMessage> MessagesToKeep,
    string? AnchorContentId,
    bool IsSplitTurn,
    AgentTokenEstimate TokensBefore,
    string? PreviousSummary,
    AgentConversationMessage? OversizedAnchorMessage = null);

// 类型：压缩执行结果，包含摘要内容、Token 变化、调用次数、序列化统计及文件列表等详细指标
internal sealed record AgentCompactionResult(
    string Summary,
    string? AnchorContentId,
    bool IsSplitTurn,
    bool OversizedAnchorReduced,
    long TokensBefore,
    long? TokensAfter,
    int MessagesSummarized,
    int ChunkCount,
    int SummaryCallCount,
    int SummaryMaxOutputTokens,
    long SummaryPromptInputTokens,
    int SummaryPromptIncludedMessages,
    int SummaryPromptTotalMessages,
    double? CompressionRatio,
    AgentCompactionSerializerStatistics SerializerStatistics,
    IReadOnlyList<string> ReadFiles,
    IReadOnlyList<string> ModifiedFiles,
    double? TargetRatio = null,
    long? TargetTokens = null,
    bool? TargetMet = null,
    string? TargetMissReason = null,
    int? PlanningAttemptCount = null,
    double? PostCompactionInputRatio = null,
    long? CheckpointTokens = null,
    long? FixedPromptTokens = null,
    long? RetainedMessageTokens = null,
    int? ModelVisibleReadFileCount = null,
    int? ModelVisibleModifiedFileCount = null);

// 类型：摘要生成请求，包含提供者、会话、模型、系统消息、用户消息及输出 Token 上限
internal sealed record AgentCompactionSummaryRequest(
    ModelProviderId ProviderId,
    ModelProviderRuntimeDescriptor Provider,
    string SessionId,
    string? ModelId,
    AgentModelInfo? ModelInfo,
    string? WorkingDirectory,
    AgentSessionState State,
    string SystemMessage,
    string UserMessage,
    int MaxOutputTokens);

// 类型：摘要生成响应，包含摘要文本和本次调用的 Token 使用量
internal sealed record AgentCompactionSummaryResponse(
    string Summary,
    AgentSessionUsage? Usage);

// 类型：序列化统计，记录压缩过程中工具调用、工具结果、推理内容和附件的序列化与省略数量
internal sealed record AgentCompactionSerializerStatistics(
    int OmittedToolResultCount,
    int OmittedReasoningCount,
    int OmittedAttachmentCount,
    int DroppedMessageCount,
    int SerializedToolResultCharacters,
    int SerializedReasoningCharacters,
    bool ReducedOversizedAnchor,
    int TotalToolCallCount = 0,
    int SerializedToolCallCount = 0,
    int CollapsedToolCallCount = 0,
    int TotalToolResultCount = 0,
    int SerializedToolResultCount = 0,
    int SerializedToolResultExcerptCount = 0,
    int TotalReasoningCount = 0,
    int SerializedReasoningCount = 0,
    int TotalAttachmentCount = 0,
    int SerializedAttachmentCount = 0);

// 类型：序列化结果，包含最终用户消息、估算输入 Token 数、已包含/总计消息数及序列化统计
internal sealed record AgentCompactionSerializationResult(
    string UserMessage,
    long EstimatedInputTokens,
    int IncludedMessageCount,
    int TotalMessageCount,
    AgentCompactionSerializerStatistics Statistics);

// 类型：压缩摘要执行器接口，负责向模型发送摘要请求并返回摘要响应
internal interface IAgentCompactionSummaryExecutor
{
    // 函数功能：执行摘要生成请求，返回包含摘要文本和 Token 用量的响应
    Task<AgentCompactionSummaryResponse> ExecuteAsync(
        AgentCompactionSummaryRequest request,
        CancellationToken cancellationToken = default);
}
