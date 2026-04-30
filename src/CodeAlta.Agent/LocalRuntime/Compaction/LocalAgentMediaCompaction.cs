namespace CodeAlta.Agent.LocalRuntime.Compaction;

internal static class LocalAgentMediaCompaction
{
    public static bool ContainsPrunableInlineImages(
        IReadOnlyList<LocalAgentConversationMessage> messages,
        Func<LocalAgentConversationMessage, bool>? preserveMessage = null)
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
                if (part is LocalAgentMessagePart.Data data && IsImage(data.MediaType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static LocalAgentInlineMediaPruneResult PruneInlineImages(
        IReadOnlyList<LocalAgentConversationMessage> messages,
        Func<LocalAgentConversationMessage, bool>? preserveMessage = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        LocalAgentConversationMessage[]? rewrittenMessages = null;
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

            List<LocalAgentMessagePart>? rewrittenParts = null;
            for (var partIndex = 0; partIndex < message.Parts.Count; partIndex++)
            {
                var part = message.Parts[partIndex];
                if (part is LocalAgentMessagePart.Data data && IsImage(data.MediaType))
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
            rewrittenMessages[messageIndex] = new LocalAgentConversationMessage(message.Role, rewrittenParts);
        }

        return new LocalAgentInlineMediaPruneResult(
            rewrittenMessages ?? messages,
            prunedImageCount,
            prunedBase64Characters);
    }

    public static bool IsImage(string? mediaType)
        => mediaType is not null &&
           mediaType.Trim().StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static List<LocalAgentMessagePart> CopyPriorParts(
        IReadOnlyList<LocalAgentMessagePart> parts,
        int count)
    {
        var rewrittenParts = new List<LocalAgentMessagePart>(parts.Count);
        for (var index = 0; index < count; index++)
        {
            rewrittenParts.Add(parts[index]);
        }

        return rewrittenParts;
    }

    private static LocalAgentConversationMessage[] CopyPriorMessages(
        IReadOnlyList<LocalAgentConversationMessage> messages,
        int count)
    {
        var rewrittenMessages = new LocalAgentConversationMessage[messages.Count];
        for (var index = 0; index < count; index++)
        {
            rewrittenMessages[index] = messages[index];
        }

        return rewrittenMessages;
    }

    private static LocalAgentMessagePart.Text CreateOmittedImagePlaceholder(LocalAgentMessagePart.Data data)
    {
        var name = string.IsNullOrWhiteSpace(data.Name) ? "image" : data.Name.Trim();
        var mediaType = string.IsNullOrWhiteSpace(data.MediaType) ? "image/*" : data.MediaType.Trim();
        var byteCount = EstimateDecodedByteCount(data.Base64Data);
        var sizeDescription = byteCount is > 0
            ? $"approximately {byteCount.Value} bytes"
            : $"{data.Base64Data.Length} base64 characters";
        return new LocalAgentMessagePart.Text(
            $"[Image attachment omitted from retained context: {name}; mediaType={mediaType}; originalSize={sizeDescription}.]");
    }

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

internal sealed record LocalAgentInlineMediaPruneResult(
    IReadOnlyList<LocalAgentConversationMessage> Messages,
    int PrunedImageCount,
    long PrunedBase64Characters);
