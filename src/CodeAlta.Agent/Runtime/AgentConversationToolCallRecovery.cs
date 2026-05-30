namespace CodeAlta.Agent.Runtime;

internal static class AgentConversationToolCallRecovery
{
    private const string RecoveredToolOutputPrefix = "aborted";

    public static IReadOnlyList<AgentConversationMessage> NormalizeForReplay(
        IReadOnlyList<AgentConversationMessage> conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

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

            var missingToolResults = message.Role == AgentConversationRole.Assistant
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
