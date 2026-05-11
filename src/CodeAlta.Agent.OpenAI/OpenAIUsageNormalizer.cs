namespace CodeAlta.Agent.OpenAI;

internal static class OpenAIUsageNormalizer
{
    public static long? GetFreshInputTokens(long? inputTokens, long? cachedInputTokens)
    {
        if (inputTokens is null || cachedInputTokens is not > 0)
        {
            return inputTokens;
        }

        return Math.Max(0, inputTokens.Value - cachedInputTokens.Value);
    }

    public static long? GetTotalTokens(long? totalTokens, long? rawInputTokens, long? outputTokens)
    {
        var rawTotalTokens = Sum(rawInputTokens, outputTokens);
        if (totalTokens is null)
        {
            return rawTotalTokens;
        }

        return rawTotalTokens is { } rawTotal
            ? Math.Max(totalTokens.Value, rawTotal)
            : totalTokens;
    }

    private static long? Sum(long? left, long? right)
    {
        if (left is null && right is null)
        {
            return null;
        }

        return (left ?? 0) + (right ?? 0);
    }
}
