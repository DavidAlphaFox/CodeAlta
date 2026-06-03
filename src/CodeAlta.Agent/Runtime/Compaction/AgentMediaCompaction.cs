namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：检测并裁剪对话中可删减的内联图片，以节省 Token 占用
internal static class AgentMediaCompaction
{
    // 函数功能：判断消息列表中是否存在可裁剪的内联图片（允许通过 preserveMessage 跳过特定消息）
    public static bool ContainsPrunableInlineImages(
        IReadOnlyList<AgentConversationMessage> messages,
        Func<AgentConversationMessage, bool>? preserveMessage = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages)
        {
            if (preserveMessage?.Invoke(message) == true)
            {
                continue;
            }

            foreach (var part in message.Parts)
            {
                if (part is AgentMessagePart.Data data && IsImage(data.MediaType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // 函数功能：将消息中的内联图片替换为文字占位符，返回修改后的消息列表及裁剪统计（图片数、字符数）
    public static AgentInlineMediaPruneResult PruneInlineImages(
        IReadOnlyList<AgentConversationMessage> messages,
        Func<AgentConversationMessage, bool>? preserveMessage = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        AgentConversationMessage[]? rewrittenMessages = null;
        var prunedImageCount = 0;
        var prunedBase64Characters = 0L;

        for (var messageIndex = 0; messageIndex < messages.Count; messageIndex++)
        {
            var message = messages[messageIndex];
            if (preserveMessage?.Invoke(message) == true)
            {
                rewrittenMessages?[messageIndex] = message;
                continue;
            }

            List<AgentMessagePart>? rewrittenParts = null;
            for (var partIndex = 0; partIndex < message.Parts.Count; partIndex++)
            {
                var part = message.Parts[partIndex];
                if (part is AgentMessagePart.Data data && IsImage(data.MediaType))
                {
                    rewrittenParts ??= CopyPriorParts(message.Parts, partIndex);
                    rewrittenParts.Add(CreateOmittedImagePlaceholder(data));
                    prunedImageCount++;
                    prunedBase64Characters += data.Base64Data.Length;
                    continue;
                }

                rewrittenParts?.Add(part);
            }

            if (rewrittenParts is null)
            {
                rewrittenMessages?[messageIndex] = message;
                continue;
            }

            rewrittenMessages ??= CopyPriorMessages(messages, messageIndex);
            rewrittenMessages[messageIndex] = new AgentConversationMessage(message.Role, rewrittenParts);
        }

        return new AgentInlineMediaPruneResult(
            rewrittenMessages ?? messages,
            prunedImageCount,
            prunedBase64Characters);
    }

    // 函数功能：判断 MIME 类型字符串是否为图片类型（以 "image/" 开头）
    public static bool IsImage(string? mediaType)
        => mediaType is not null &&
           mediaType.Trim().StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    // 函数功能：将消息部分列表的前 count 项复制到新列表，用于懒写时复制
    private static List<AgentMessagePart> CopyPriorParts(
        IReadOnlyList<AgentMessagePart> parts,
        int count)
    {
        var rewrittenParts = new List<AgentMessagePart>(parts.Count);
        for (var index = 0; index < count; index++)
        {
            rewrittenParts.Add(parts[index]);
        }

        return rewrittenParts;
    }

    // 函数功能：将消息列表的前 count 条复制到新数组，数组长度与原列表相同，用于懒写时复制
    private static AgentConversationMessage[] CopyPriorMessages(
        IReadOnlyList<AgentConversationMessage> messages,
        int count)
    {
        var rewrittenMessages = new AgentConversationMessage[messages.Count];
        for (var index = 0; index < count; index++)
        {
            rewrittenMessages[index] = messages[index];
        }

        return rewrittenMessages;
    }

    // 函数功能：根据被裁剪图片的名称、MIME 类型和估算大小，生成描述性的文本占位符消息部分
    private static AgentMessagePart.Text CreateOmittedImagePlaceholder(AgentMessagePart.Data data)
    {
        var name = string.IsNullOrWhiteSpace(data.Name) ? "image" : data.Name.Trim();
        var mediaType = string.IsNullOrWhiteSpace(data.MediaType) ? "image/*" : data.MediaType.Trim();
        var byteCount = EstimateDecodedByteCount(data.Base64Data);
        var sizeDescription = byteCount is > 0
            ? $"approximately {byteCount.Value} bytes"
            : $"{data.Base64Data.Length} base64 characters";
        return new AgentMessagePart.Text(
            $"[Image attachment omitted from retained context: {name}; mediaType={mediaType}; originalSize={sizeDescription}.]");
    }

    // 函数功能：根据 Base64 字符串长度和填充字符数估算解码后的字节数，无效数据返回 null
    private static long? EstimateDecodedByteCount(string base64Data)
    {
        if (string.IsNullOrWhiteSpace(base64Data))
        {
            return 0;
        }

        var length = base64Data.Length;
        while (length > 0 && char.IsWhiteSpace(base64Data[length - 1]))
        {
            length--;
        }

        var padding = 0;
        for (var index = length - 1; index >= 0 && base64Data[index] == '='; index--)
        {
            padding++;
        }

        if (length < padding)
        {
            return null;
        }

        return Math.Max(0, (length * 3L / 4L) - padding);
    }
}

// 类型：内联图片裁剪结果，包含重写后的消息列表、裁剪图片数量及裁剪的 Base64 字符数
internal sealed record AgentInlineMediaPruneResult(
    IReadOnlyList<AgentConversationMessage> Messages,
    int PrunedImageCount,
    long PrunedBase64Characters);
