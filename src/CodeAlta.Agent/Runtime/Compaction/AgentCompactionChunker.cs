namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：将对话消息按 Token 预算切分为可独立压缩的块，并处理超大文本消息的拆分
internal static class AgentCompactionChunker
{
    // 函数功能：将消息列表按最大 Token 数切分为多个块，每块不超过 maxInputTokens；超大文本消息会先拆分
    public static IReadOnlyList<IReadOnlyList<AgentConversationMessage>> CreateChunks(
        IReadOnlyList<AgentConversationMessage> messages,
        int maxInputTokens,
        Func<IReadOnlyList<AgentConversationMessage>, long> estimateChunkTokens)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(estimateChunkTokens);

        if (messages.Count == 0)
        {
            return [];
        }

        var normalizedMessages = ExpandOversizedTextMessages(messages, maxInputTokens);
        var normalizedUnits = AgentCompactionCanonicalizer.Normalize(normalizedMessages);
        var chunks = new List<IReadOnlyList<AgentConversationMessage>>();
        var currentChunk = new List<AgentCompactionUnit>();

        foreach (var unit in normalizedUnits)
        {
            currentChunk.Add(unit);
            if (currentChunk.Count == 1)
            {
                continue;
            }

            if (estimateChunkTokens(FlattenUnits(currentChunk)) <= maxInputTokens)
            {
                continue;
            }

            currentChunk.RemoveAt(currentChunk.Count - 1);
            chunks.Add(FlattenUnits(currentChunk));
            currentChunk = [unit];
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(FlattenUnits(currentChunk));
        }

        return chunks;
    }

    // 函数功能：将超过字符预算的单一文本/推理消息拆分为多条小消息，返回展开后的列表
    private static IReadOnlyList<AgentConversationMessage> ExpandOversizedTextMessages(
        IReadOnlyList<AgentConversationMessage> messages,
        int maxInputTokens)
    {
        if (maxInputTokens <= 0)
        {
            return messages;
        }

        var maxChunkCharacters = Math.Max(maxInputTokens * 4, 256);
        var expanded = new List<AgentConversationMessage>(messages.Count);
        foreach (var message in messages)
        {
            if (!TrySplitMessage(message, maxChunkCharacters, out var splitMessages))
            {
                expanded.Add(message);
                continue;
            }

            expanded.AddRange(splitMessages);
        }

        return expanded;
    }

    // 函数功能：将压缩单元列表展开为原始消息数组
    private static IReadOnlyList<AgentConversationMessage> FlattenUnits(IReadOnlyList<AgentCompactionUnit> units)
        => units.SelectMany(static unit => unit.SourceMessages).ToArray();

    // 函数功能：尝试将单部分的超大文本或推理消息拆分；成功时返回 true 并通过 out 输出拆分后的消息列表
    private static bool TrySplitMessage(
        AgentConversationMessage message,
        int maxChunkCharacters,
        out IReadOnlyList<AgentConversationMessage> splitMessages)
    {
        if (message.Parts.Count != 1)
        {
            splitMessages = [];
            return false;
        }

        switch (message.Parts[0])
        {
            case AgentMessagePart.Text text when text.Value.Length > maxChunkCharacters:
                splitMessages = SplitTextMessage(message.Role, text.Value, maxChunkCharacters);
                return true;
            case AgentMessagePart.Reasoning reasoning when !string.IsNullOrWhiteSpace(reasoning.Value) && reasoning.Value.Length > maxChunkCharacters:
                splitMessages = SplitReasoningMessage(message.Role, reasoning.Value!, reasoning.ProtectedData, maxChunkCharacters);
                return true;
            default:
                splitMessages = [];
                return false;
        }
    }

    // 函数功能：将长文本内容按字符预算拆分为多条文本消息
    private static IReadOnlyList<AgentConversationMessage> SplitTextMessage(
        AgentConversationRole role,
        string value,
        int maxChunkCharacters)
        => SplitByCharacterBudget(
            value,
            maxChunkCharacters,
            chunk => new AgentConversationMessage(
                role,
                [new AgentMessagePart.Text(chunk)]));

    // 函数功能：将长推理内容按字符预算拆分为多条推理消息（protectedData 仅保留在首条）
    private static IReadOnlyList<AgentConversationMessage> SplitReasoningMessage(
        AgentConversationRole role,
        string value,
        string? protectedData,
        int maxChunkCharacters)
        => SplitByCharacterBudget(
            value,
            maxChunkCharacters,
            chunk => new AgentConversationMessage(
                role,
                [new AgentMessagePart.Reasoning(chunk, ProtectedData: null)]));

    // 函数功能：按字符预算将字符串切分为多个块，优先在换行或空白处断开，再逐一调用 createMessage 构建消息列表
    private static IReadOnlyList<AgentConversationMessage> SplitByCharacterBudget(
        string value,
        int maxChunkCharacters,
        Func<string, AgentConversationMessage> createMessage)
    {
        var parts = new List<AgentConversationMessage>();
        var start = 0;
        while (start < value.Length)
        {
            var length = Math.Min(maxChunkCharacters, value.Length - start);
            var end = start + length;
            if (end < value.Length)
            {
                var breakIndex = value.LastIndexOfAny(['\n', '\r', ' ', '\t'], end - 1, length);
                if (breakIndex > start + (length / 2))
                {
                    end = breakIndex + 1;
                }
            }

            var chunk = value[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                parts.Add(createMessage(chunk));
            }

            start = end;
        }

        return parts;
    }
}
