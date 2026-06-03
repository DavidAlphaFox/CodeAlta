namespace CodeAlta.Agent.Runtime;

// 模块功能：修复对话中工具调用与工具结果的孤立/缺失问题，确保对话可安全回放
internal static class AgentConversationToolCallRecovery
{
    private const string RecoveredToolOutputPrefix = "aborted";

    // 函数功能：规范化对话以供回放，删除孤立的工具结果，并在指定索引前为缺失结果补充占位回复
    public static IReadOnlyList<AgentConversationMessage> NormalizeForReplay(
        IReadOnlyList<AgentConversationMessage> conversation,
        int recoverMissingToolResultsBeforeIndex = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        recoverMissingToolResultsBeforeIndex = Math.Clamp(recoverMissingToolResultsBeforeIndex, 0, conversation.Count);

        var toolCallIds = new HashSet<string>(StringComparer.Ordinal);
        var toolResultIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in conversation)
        {
            if (message.Role == AgentConversationRole.Assistant)
            {
                foreach (var toolCall in message.Parts.OfType<AgentMessagePart.ToolCall>())
                {
                    if (!string.IsNullOrWhiteSpace(toolCall.CallId))
                    {
                        toolCallIds.Add(toolCall.CallId);
                    }
                }
            }
            else if (message.Role == AgentConversationRole.Tool)
            {
                foreach (var toolResult in message.Parts.OfType<AgentMessagePart.ToolResult>())
                {
                    if (!string.IsNullOrWhiteSpace(toolResult.CallId))
                    {
                        toolResultIds.Add(toolResult.CallId);
                    }
                }
            }
        }

        List<AgentConversationMessage>? normalized = null;
        for (var messageIndex = 0; messageIndex < conversation.Count; messageIndex++)
        {
            var message = conversation[messageIndex];
            var normalizedMessage = RemoveOrphanToolResults(message, toolCallIds);
            if (normalizedMessage is null)
            {
                normalized ??= CopyPriorMessages(conversation, messageIndex);
                continue;
            }

            var missingToolResults = message.Role == AgentConversationRole.Assistant &&
                                     messageIndex < recoverMissingToolResultsBeforeIndex
                ? CreateMissingToolResults(message.Parts, toolResultIds)
                : [];
            if (missingToolResults.Count > 0)
            {
                normalized ??= CopyPriorMessages(conversation, messageIndex);
                normalized.Add(normalizedMessage);
                normalized.Add(new AgentConversationMessage(AgentConversationRole.Tool, missingToolResults));
                continue;
            }

            normalized?.Add(normalizedMessage);
        }

        return normalized ?? conversation;
    }

    // 函数功能：从消息中移除没有对应工具调用的孤立工具结果，消息为空时返回 null
    private static AgentConversationMessage? RemoveOrphanToolResults(
        AgentConversationMessage message,
        HashSet<string> toolCallIds)
    {
        if (message.Role != AgentConversationRole.Tool)
        {
            return message;
        }

        List<AgentMessagePart>? parts = null;
        for (var partIndex = 0; partIndex < message.Parts.Count; partIndex++)
        {
            var part = message.Parts[partIndex];
            if (part is AgentMessagePart.ToolResult toolResult &&
                (string.IsNullOrWhiteSpace(toolResult.CallId) || !toolCallIds.Contains(toolResult.CallId)))
            {
                parts ??= CopyPriorParts(message.Parts, partIndex);
                continue;
            }

            parts?.Add(part);
        }

        if (parts is null)
        {
            return message;
        }

        return parts.Count == 0
            ? null
            : message with { Parts = parts };
    }

    // 函数功能：为助手消息中没有对应结果的工具调用创建"已中止"占位工具结果列表
    private static IReadOnlyList<AgentMessagePart> CreateMissingToolResults(
        IReadOnlyList<AgentMessagePart> parts,
        HashSet<string> toolResultIds)
    {
        List<AgentMessagePart>? toolResults = null;
        foreach (var toolCall in parts.OfType<AgentMessagePart.ToolCall>())
        {
            if (string.IsNullOrWhiteSpace(toolCall.CallId) || toolResultIds.Contains(toolCall.CallId))
            {
                continue;
            }

            toolResults ??= [];
            toolResults.Add(new AgentMessagePart.ToolResult(toolCall.CallId, CreateMissingToolResult(toolCall)));
        }

        return toolResults ?? [];
    }

    // 函数功能：为被中断的工具调用生成标准错误占位结果，标记 Success=false 并附带说明信息
    private static AgentToolResult CreateMissingToolResult(AgentMessagePart.ToolCall toolCall)
    {
        var toolName = string.IsNullOrWhiteSpace(toolCall.Name)
            ? "unknown_tool"
            : toolCall.Name.Trim();
        var message = $"{RecoveredToolOutputPrefix}: CodeAlta did not record an output for interrupted tool call '{toolName}' ({toolCall.CallId}).";
        return new AgentToolResult(
            Success: false,
            Items: [new AgentToolResultItem.Text(message)],
            Error: message);
    }

    // 函数功能：将对话的前 count 条消息复制到新列表，用于懒写时复制
    private static List<AgentConversationMessage> CopyPriorMessages(
        IReadOnlyList<AgentConversationMessage> conversation,
        int count)
    {
        var copy = new List<AgentConversationMessage>(conversation.Count + 1);
        for (var index = 0; index < count; index++)
        {
            copy.Add(conversation[index]);
        }

        return copy;
    }

    // 函数功能：将消息部分列表的前 count 项复制到新列表，用于懒写时复制
    private static List<AgentMessagePart> CopyPriorParts(
        IReadOnlyList<AgentMessagePart> parts,
        int count)
    {
        var copy = new List<AgentMessagePart>(parts.Count);
        for (var index = 0; index < count; index++)
        {
            copy.Add(parts[index]);
        }

        return copy;
    }
}
