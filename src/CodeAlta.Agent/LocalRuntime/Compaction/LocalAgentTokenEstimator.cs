using System.Text;

namespace CodeAlta.Agent.LocalRuntime.Compaction;

internal static class LocalAgentTokenEstimator
{
    public static LocalAgentTokenEstimate EstimatePromptTokens(
        string? systemMessage,
        string? developerInstructions,
        IReadOnlyList<LocalAgentConversationMessage> conversation,
        AgentSessionUsage? usage)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var expectedMessageCount = conversation.Count;
        if (usage?.Window is { CurrentTokens: > 0 } window &&
            window.MessageCount == expectedMessageCount)
        {
            return new LocalAgentTokenEstimate(window.CurrentTokens.Value, "provider-window", IsEstimated: false);
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

        return new LocalAgentTokenEstimate(Math.Max(estimatedTokens, 1), "local-heuristic", IsEstimated: true);
    }

    public static long EstimateMessage(LocalAgentConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var total = 6L;
        foreach (var part in message.Parts)
        {
            total += EstimatePart(part);
        }

        return total;
    }

    public static long EstimateCheckpointTokens(string summary)
        => EstimateText(summary) + 16;

    public static long EstimateTextTokens(string? text)
        => EstimateText(text);

    private static bool HasLeadingCheckpoint(IReadOnlyList<LocalAgentConversationMessage> conversation)
        => conversation.Count > 0 && LocalAgentCompactionCheckpoint.TryExtractSummary(conversation[0]) is not null;

    private static bool TryGetLastOperationWindowEstimate(
        IReadOnlyList<LocalAgentConversationMessage> conversation,
        AgentOperationUsageSnapshot lastOperation,
        out LocalAgentTokenEstimate estimate)
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

        estimate = new LocalAgentTokenEstimate(
            baselineTokens.Value + trailingTokens,
            "provider-last-operation+local-tail",
            IsEstimated: true);
        return true;
    }

    private static long EstimatePart(LocalAgentMessagePart part)
    {
        return part switch
        {
            LocalAgentMessagePart.Text text => EstimateText(text.Value) + 4,
            LocalAgentMessagePart.Reasoning reasoning => EstimateText(reasoning.Value) + EstimateText(reasoning.ProtectedData) + 8,
            LocalAgentMessagePart.ToolCall toolCall => EstimateText(toolCall.Name) + EstimateText(toolCall.Arguments.GetRawText()) + 16,
            LocalAgentMessagePart.ToolResult toolResult => EstimateToolResult(toolResult.Result) + 16,
            LocalAgentMessagePart.Uri uri => EstimateText(uri.Value) + EstimateText(uri.MediaType) + EstimateText(uri.Name) + 8,
            LocalAgentMessagePart.Data data => EstimateText(data.Name) + EstimateText(data.MediaType) + Math.Max(data.Base64Data.Length / 8, 32),
            _ => 4,
        };
    }

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
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var condensedLength = text.Trim().Length;
        return Math.Max((condensedLength + 3) / 4, 1);
    }

    private static long? Sum(long? left, long? right)
        => left.HasValue || right.HasValue ? (left ?? 0) + (right ?? 0) : null;

    private static int FindLastAssistantMessageIndex(IReadOnlyList<LocalAgentConversationMessage> conversation)
    {
        for (var index = conversation.Count - 1; index >= 0; index--)
        {
            if (conversation[index].Role is LocalAgentConversationRole.Assistant)
            {
                return index;
            }
        }

        return -1;
    }
}
