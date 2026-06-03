namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：从模型能力元数据中解析上下文窗口、输入/输出 Token 上限，计算压缩所需的 Token 预算
internal static class AgentTokenBudgetResolver
{
    // 说明：用于查找上下文窗口大小的能力键名（按优先级排列）
    private static readonly string[] ContextWindowCapabilityKeys =
    [
        "contextWindow",
        "contextWindowTokens",
        "context_length",
        "contextLength",
        "tokenLimit",
    ];

    // 说明：用于查找最大输入 Token 数的能力键名
    private static readonly string[] InputTokenCapabilityKeys =
    [
        "inputTokenLimit",
        "maxInputTokens",
    ];

    // 说明：用于查找最大输出 Token 数的能力键名
    private static readonly string[] OutputTokenCapabilityKeys =
    [
        "outputTokenLimit",
        "maxOutputTokens",
        "maxTokens",
    ];

    // 函数功能：从模型信息能力表中读取各限制值，计算并返回完整的 AgentTokenBudget
    public static AgentTokenBudget Resolve(
        AgentModelInfo? modelInfo,
        AgentCompactionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var contextWindow = TryReadCapability(modelInfo, ContextWindowCapabilityKeys);
        var inputTokenLimit = TryReadCapability(modelInfo, InputTokenCapabilityKeys);
        var outputTokenLimit = TryReadCapability(modelInfo, OutputTokenCapabilityKeys);
        var inputContextLimit = ResolveInputContextLimit(contextWindow, inputTokenLimit, outputTokenLimit);

        return new AgentTokenBudget(
            TotalContextEnvelope: contextWindow,
            InputContextLimit: inputContextLimit,
            MaxOutputTokens: outputTokenLimit);
    }

    // 函数功能：根据上下文窗口、输入限制和输出限制推断实际可用的输入上下文 Token 数上限
    internal static long? ResolveInputContextLimit(long? contextWindow, long? inputTokenLimit, long? outputTokenLimit)
    {
        if (inputTokenLimit is > 0)
        {
            return inputTokenLimit.Value;
        }

        if (contextWindow is not > 0)
        {
            return null;
        }

        if (outputTokenLimit is > 0 && outputTokenLimit.Value < contextWindow.Value)
        {
            return Math.Max(contextWindow.Value - outputTokenLimit.Value, 1L);
        }

        return contextWindow.Value;
    }

    // 函数功能：按优先级依次查找能力键，将首个有效正整数值转换为 long 返回；未找到时返回 null
    internal static long? TryReadCapability(AgentModelInfo? modelInfo, IReadOnlyList<string> keys)
    {
        if (modelInfo?.Capabilities is not { Count: > 0 } capabilities)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (!capabilities.TryGetValue(key, out var rawValue))
            {
                continue;
            }

            if (TryConvertToInt64(rawValue, out var value) && value > 0)
            {
                return value;
            }
        }

        return null;
    }

    // 函数功能：将各种数值类型或数字字符串安全转换为 long；不支持的类型返回 false
    private static bool TryConvertToInt64(object? value, out long converted)
    {
        switch (value)
        {
            case byte byteValue:
                converted = byteValue;
                return true;
            case sbyte sbyteValue:
                converted = sbyteValue;
                return true;
            case short shortValue:
                converted = shortValue;
                return true;
            case ushort ushortValue:
                converted = ushortValue;
                return true;
            case int intValue:
                converted = intValue;
                return true;
            case uint uintValue:
                converted = (long)uintValue;
                return true;
            case long longValue:
                converted = longValue;
                return true;
            case ulong ulongValue when ulongValue <= long.MaxValue:
                converted = (long)ulongValue;
                return true;
            case float floatValue when floatValue is >= long.MinValue and <= long.MaxValue:
                converted = (long)floatValue;
                return true;
            case double doubleValue when doubleValue is >= long.MinValue and <= long.MaxValue:
                converted = (long)doubleValue;
                return true;
            case decimal decimalValue when decimalValue is >= long.MinValue and <= long.MaxValue:
                converted = (long)decimalValue;
                return true;
            case string stringValue when long.TryParse(stringValue, out var parsed):
                converted = parsed;
                return true;
            default:
                converted = default;
                return false;
        }
    }
}
