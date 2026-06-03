namespace CodeAlta.Agent.Runtime;

// 模块功能：记录产生推理内容的提供者/模型来源，并提供安全回放工具
/// <summary>
/// Captures the provider/model identity that produced replayable reasoning content.
/// </summary>
/// <param name="ProtocolFamily">The provider protocol family used for the turn.</param>
/// <param name="ProviderKey">The configured provider key used for the turn.</param>
/// <param name="TransportKind">The provider transport kind used for the turn.</param>
/// <param name="ModelId">The model identifier used for the turn.</param>
public sealed record AgentReasoningProvenance(
    string ProtocolFamily,
    string ProviderKey,
    AgentTransportKind TransportKind,
    string? ModelId);

/// <summary>
/// Provides helpers for safe provider reasoning replay.
/// </summary>
public static class AgentReasoningReplay
{
    // 函数功能：为指定的提供者轮次请求创建推理来源信息，返回 AgentReasoningProvenance
    /// <summary>
    /// Creates reasoning provenance for a provider turn request.
    /// </summary>
    /// <param name="request">The provider turn request.</param>
    /// <returns>The provider/model provenance for reasoning emitted by the turn.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    public static AgentReasoningProvenance CreateProvenance(AgentTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AgentReasoningProvenance(
            request.Provider.ProtocolFamily,
            request.Provider.ProviderKey,
            request.Provider.TransportKind,
            request.ModelId);
    }

    // 函数功能：返回对当前提供者安全的对话列表，将不兼容来源的推理部分降级为文本摘要
    /// <summary>
    /// Returns a provider-safe conversation where reasoning parts from incompatible or unknown providers/models are downgraded.
    /// </summary>
    /// <param name="conversation">The replayable conversation.</param>
    /// <param name="request">The provider turn request.</param>
    /// <returns>A conversation safe to send to the provider/model in <paramref name="request" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="conversation" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<AgentConversationMessage> SanitizeForRequest(
        IReadOnlyList<AgentConversationMessage> conversation,
        AgentTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(request);

        AgentConversationMessage[]? sanitizedMessages = null;
        for (var messageIndex = 0; messageIndex < conversation.Count; messageIndex++)
        {
            var message = conversation[messageIndex];
            var sanitizedParts = SanitizeParts(message.Parts, request, out var changed);
            if (!changed)
            {
                if (sanitizedMessages is not null)
                {
                    sanitizedMessages[messageIndex] = message;
                }

                continue;
            }

            sanitizedMessages ??= CopyPrefix(conversation, messageIndex);
            sanitizedMessages[messageIndex] = message with { Parts = sanitizedParts };
        }

        return sanitizedMessages ?? conversation;
    }

    // 函数功能：将对话列表的前 count 条消息复制到新数组并返回，用于懒写时复制
    private static AgentConversationMessage[] CopyPrefix(
        IReadOnlyList<AgentConversationMessage> conversation,
        int count)
    {
        var copy = new AgentConversationMessage[conversation.Count];
        for (var index = 0; index < count; index++)
        {
            copy[index] = conversation[index];
        }

        return copy;
    }

    // 函数功能：对消息的所有部分执行兼容性清洗，通过 out changed 指示是否有修改
    private static IReadOnlyList<AgentMessagePart> SanitizeParts(
        IReadOnlyList<AgentMessagePart> parts,
        AgentTurnRequest request,
        out bool changed)
    {
        List<AgentMessagePart>? sanitizedParts = null;
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var sanitized = SanitizePart(part, request, out var partChanged);
            if (!partChanged)
            {
                sanitizedParts?.Add(part);
                continue;
            }

            sanitizedParts ??= CopyPrefix(parts, partIndex);
            if (sanitized is not null)
            {
                sanitizedParts.Add(sanitized);
            }
        }

        changed = sanitizedParts is not null;
        return sanitizedParts ?? parts;
    }

    // 函数功能：将消息部分列表的前 count 项复制到新列表并返回
    private static List<AgentMessagePart> CopyPrefix(IReadOnlyList<AgentMessagePart> parts, int count)
    {
        var copy = new List<AgentMessagePart>(parts.Count);
        for (var index = 0; index < count; index++)
        {
            copy.Add(parts[index]);
        }

        return copy;
    }

    // 函数功能：对单个消息部分执行兼容性检查，不兼容的推理部分降级为文本或丢弃；out changed 标记是否变化
    private static AgentMessagePart? SanitizePart(
        AgentMessagePart part,
        AgentTurnRequest request,
        out bool changed)
    {
        if (part is not AgentMessagePart.Reasoning reasoning)
        {
            changed = false;
            return part;
        }

        if (IsCompatible(reasoning.Provenance, request))
        {
            changed = false;
            return part;
        }

        changed = true;
        return string.IsNullOrWhiteSpace(reasoning.Value)
            ? null
            : new AgentMessagePart.Text(CreateReasoningSummaryText(reasoning.Value));
    }

    // 函数功能：检查推理来源是否与当前请求的提供者/传输类型/协议族/模型完全匹配
    private static bool IsCompatible(AgentReasoningProvenance? provenance, AgentTurnRequest request)
    {
        if (provenance is null || string.IsNullOrWhiteSpace(provenance.ModelId) || string.IsNullOrWhiteSpace(request.ModelId))
        {
            return false;
        }

        return provenance.TransportKind == request.Provider.TransportKind &&
               string.Equals(provenance.ProtocolFamily, request.Provider.ProtocolFamily, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(provenance.ProviderKey, request.Provider.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(provenance.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase);
    }

    // 函数功能：将推理内容包裹在 XML 摘要标签中，生成降级后的文本表示
    private static string CreateReasoningSummaryText(string value)
        => $"<assistant_reasoning_summary>{value}</assistant_reasoning_summary>";
}
