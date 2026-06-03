using System.Text;

namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：基于启发式规则估算对话 prompt 的 token 用量，支持窗口快照与历史操作快照两种快速路径
internal static class AgentTokenEstimator
{
    // 函数功能：估算完整 prompt 的 token 数，优先使用窗口快照或上次操作快照，回退到逐消息累计估算
    public static AgentTokenEstimate EstimatePromptTokens(
        string? systemMessage,
        string? developerInstructions,
        IReadOnlyList<AgentConversationMessage> conversation,
        AgentSessionUsage? usage)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (TryGetWindowSnapshotEstimate(conversation, usage, out var windowEstimate))
        {
            return windowEstimate;
        }

        if (!HasLeadingCheckpoint(conversation) &&
            usage?.LastOperation is { } lastOperation &&
            TryGetLastOperationWindowEstimate(conversation, lastOperation, out var lastOperationEstimate))
        {
            return lastOperationEstimate;
        }

        var estimatedTokens = 0L;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            estimatedTokens += EstimateText(systemMessage) + 8;
        }

        if (!string.IsNullOrWhiteSpace(developerInstructions))
        {
            estimatedTokens += EstimateText(developerInstructions) + 8;
        }

        foreach (var message in conversation)
        {
            estimatedTokens += EstimateMessage(message);
        }

        return new AgentTokenEstimate(Math.Max(estimatedTokens, 1), "local-heuristic", IsEstimated: true);
    }

    // 函数功能：估算单条对话消息的 token 数，含固定开销 6 个 token
    public static long EstimateMessage(AgentConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var total = 6L;
        foreach (var part in message.Parts)
        {
            total += EstimatePart(part);
        }

        return total;
    }

    // 函数功能：估算压缩检查点摘要的 token 数，含 16 个固定开销
    public static long EstimateCheckpointTokens(string summary)
        => EstimateText(summary) + 16;

    // 函数功能：估算任意文本的 token 数，null 或空时返回 0
    public static long EstimateTextTokens(string? text)
        => EstimateText(text);

    // 函数功能：判断对话首条消息是否为压缩检查点摘要
    private static bool HasLeadingCheckpoint(IReadOnlyList<AgentConversationMessage> conversation)
        => conversation.Count > 0 && AgentCompactionCheckpoint.TryExtractSummary(conversation[0]) is not null;

    // 函数功能：尝试利用窗口快照（provider 上报的当前 token 数）加上尾部本地估算构建估算结果
    private static bool TryGetWindowSnapshotEstimate(
        IReadOnlyList<AgentConversationMessage> conversation,
        AgentSessionUsage? usage,
        out AgentTokenEstimate estimate)
    {
        if (usage?.Window is not { CurrentTokens: > 0, MessageCount: >= 0 } window)
        {
            estimate = default!;
            return false;
        }

        var messageCount = window.MessageCount!.Value;
        if (messageCount > conversation.Count)
        {
            estimate = default!;
            return false;
        }

        var trailingTokens = 0L;
        for (var index = messageCount; index < conversation.Count; index++)
        {
            trailingTokens += EstimateMessage(conversation[index]);
        }

        var isAuthoritativeWindow = string.Equals(window.Label, "Active context window", StringComparison.Ordinal);
        var source = trailingTokens == 0
            ? (isAuthoritativeWindow ? "provider-window" : "window-snapshot")
            : (isAuthoritativeWindow ? "provider-window+local-tail" : "window-snapshot+local-tail");
        estimate = new AgentTokenEstimate(
            window.CurrentTokens!.Value + trailingTokens,
            source,
            IsEstimated: trailingTokens > 0 || !isAuthoritativeWindow);
        return true;
    }

    // 函数功能：尝试用上一次操作的输入/输出 token 之和加上末尾新消息的估算构建估算结果
    private static bool TryGetLastOperationWindowEstimate(
        IReadOnlyList<AgentConversationMessage> conversation,
        AgentOperationUsageSnapshot lastOperation,
        out AgentTokenEstimate estimate)
    {
        var baselineTokens = Sum(lastOperation.InputTokens, lastOperation.OutputTokens);
        if (baselineTokens is not > 0)
        {
            estimate = default!;
            return false;
        }

        var lastAssistantIndex = FindLastAssistantMessageIndex(conversation);
        if (lastAssistantIndex < 0)
        {
            estimate = default!;
            return false;
        }

        var trailingTokens = 0L;
        for (var index = lastAssistantIndex + 1; index < conversation.Count; index++)
        {
            trailingTokens += EstimateMessage(conversation[index]);
        }

        estimate = new AgentTokenEstimate(
            baselineTokens.Value + trailingTokens,
            "provider-last-operation+local-tail",
            IsEstimated: true);
        return true;
    }

    // 函数功能：按消息部件类型分发并估算其 token 数，图片固定 1024，未知类型返回 4
    private static long EstimatePart(AgentMessagePart part)
    {
        return part switch
        {
            AgentMessagePart.Text text => EstimateText(text.Value) + 4,
            AgentMessagePart.Reasoning reasoning => EstimateText(reasoning.Value) + EstimateText(reasoning.ProtectedData) + 8,
            AgentMessagePart.ToolCall toolCall => EstimateText(toolCall.Name) + EstimateText(toolCall.Arguments.GetRawText()) + 16,
            AgentMessagePart.ToolResult toolResult => EstimateToolResult(toolResult.Result) + 16,
            AgentMessagePart.Uri uri => EstimateText(uri.Value) + EstimateText(uri.MediaType) + EstimateText(uri.Name) + 8,
            AgentMessagePart.Data data => EstimateData(data),
            _ => 4,
        };
    }

    // 函数功能：估算 Data 部件的 token 数，图片固定 1024，其他按 Base64 长度除以 8 估算
    private static long EstimateData(AgentMessagePart.Data data)
    {
        var metadataTokens = EstimateText(data.Name) + EstimateText(data.MediaType) + 8;
        if (AgentMediaCompaction.IsImage(data.MediaType))
        {
            return metadataTokens + 1_024;
        }

        return metadataTokens + Math.Max(data.Base64Data.Length / 8, 32);
    }

    // 函数功能：将工具结果的所有文本项与错误信息拼接后估算 token 数
    private static long EstimateToolResult(AgentToolResult result)
    {
        var builder = new StringBuilder();
        foreach (var item in result.Items)
        {
            switch (item)
            {
                case AgentToolResultItem.Text text:
                    builder.AppendLine(text.Value);
                    break;
                case AgentToolResultItem.ImageUrl imageUrl:
                    builder.AppendLine(imageUrl.Url);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.AppendLine(result.Error);
        }

        return EstimateText(builder.ToString());
    }

    private static long EstimateText(string? text)
        => string.IsNullOrWhiteSpace(text) ? 0 : TokenEstimator.Estimate(text.AsSpan().Trim());

    // 函数功能：对两个可空 long 求和，至少有一个有值时返回结果，否则返回 null
    private static long? Sum(long? left, long? right)
        => left.HasValue || right.HasValue ? (left ?? 0) + (right ?? 0) : null;

    // 函数功能：从末尾向前查找最后一条 Assistant 角色消息的索引，未找到返回 -1
    private static int FindLastAssistantMessageIndex(IReadOnlyList<AgentConversationMessage> conversation)
    {
        for (var index = conversation.Count - 1; index >= 0; index--)
        {
            if (conversation[index].Role is AgentConversationRole.Assistant)
            {
                return index;
            }
        }

        return -1;
    }
}
