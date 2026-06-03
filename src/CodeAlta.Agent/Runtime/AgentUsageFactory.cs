using CodeAlta.Agent.Runtime.Compaction;

namespace CodeAlta.Agent.Runtime;

// 模块功能：构建和合并 AgentSessionUsage 用量快照，封装 token 统计、上下文窗口及模型预算信息
internal static class AgentUsageFactory
{
    // 函数功能：根据模型调用的各项 token 数量创建单次操作用量快照，包含上下文窗口和最后一次操作详情
    public static AgentSessionUsage CreateOperationUsage(
        string? modelId,
        AgentModelInfo? modelInfo,
        long? inputTokens,
        long? outputTokens,
        long? totalTokens,
        long? cachedInputTokens,
        long? reasoningTokens,
        DateTimeOffset updatedAt)
    {
        var currentTokens = totalTokens ?? Sum(inputTokens, outputTokens);
        var budget = GetModelTokenBudget(modelInfo);
        var tokenLimit = budget.InputContextLimit;
        var window = currentTokens is not null || tokenLimit is not null
            ? new AgentWindowUsageSnapshot(
                CurrentTokens: currentTokens,
                TokenLimit: tokenLimit,
                MessageCount: null,
                Label: tokenLimit is not null ? "Active context window" : "Estimated active context",
                TotalContextEnvelope: budget.TotalContextEnvelope,
                MaxOutputTokens: budget.MaxOutputTokens)
            : null;

        return new AgentSessionUsage(
            Window: window,
            LastOperation: new AgentOperationUsageSnapshot(
                Model: string.IsNullOrWhiteSpace(modelId) ? modelInfo?.Id : modelId,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                CachedInputTokens: cachedInputTokens,
                ReasoningTokens: reasoningTokens,
                Label: string.IsNullOrWhiteSpace(modelId)
                    ? null
                    : $"{modelId}: {inputTokens ?? 0}/{outputTokens ?? 0} tokens"),
            Scope: window is null ? AgentUsageScope.LastOperation : AgentUsageScope.CurrentWindow,
            Source: AgentUsageSource.ProviderUsage,
            UpdatedAt: updatedAt);
    }

    // 函数功能：从历史事件列表中提取并合并所有用量更新事件，恢复最新会话用量；无更新时返回 null
    public static AgentSessionUsage? RecoverUsageFromHistory(IReadOnlyList<AgentEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        AgentSessionUsage? usage = null;
        foreach (var @event in history)
        {
            if (@event is AgentSessionUpdateEvent { Usage: { } updateUsage })
            {
                usage = MergeUsage(usage, updateUsage);
            }
        }

        return usage;
    }

    // 函数功能：将消息数量附加到用量快照的窗口信息中；messageCount 无效或窗口不存在时原样返回
    public static AgentSessionUsage? AttachMessageCount(AgentSessionUsage? usage, int? messageCount)
    {
        if (usage?.Window is null || messageCount is not >= 0)
        {
            return usage;
        }

        return usage with
        {
            Window = new AgentWindowUsageSnapshot(
                CurrentTokens: usage.Window.CurrentTokens,
                TokenLimit: usage.Window.TokenLimit,
                MessageCount: messageCount,
                Label: usage.Window.Label,
                TotalContextEnvelope: usage.Window.TotalContextEnvelope,
                MaxOutputTokens: usage.Window.MaxOutputTokens),
        };
    }

    // 函数功能：将模型 token 预算（上下文限制、最大输出等）注入到用量快照的窗口信息中
    public static AgentSessionUsage? AttachModelInfo(AgentSessionUsage? usage, AgentModelInfo? modelInfo)
    {
        if (usage is null)
        {
            return null;
        }

        var budget = GetModelTokenBudget(modelInfo);
        var tokenLimit = usage.Window?.TokenLimit ?? budget.InputContextLimit;
        if (tokenLimit is null)
        {
            return usage;
        }

        var currentTokens = usage.Window?.CurrentTokens;
        var label = usage.Window?.Label ?? "Context window limit";
        var window = new AgentWindowUsageSnapshot(
            CurrentTokens: currentTokens,
            TokenLimit: tokenLimit,
            MessageCount: usage.Window?.MessageCount,
            Label: label,
            TotalContextEnvelope: usage.Window?.TotalContextEnvelope ?? budget.TotalContextEnvelope,
            MaxOutputTokens: usage.Window?.MaxOutputTokens ?? budget.MaxOutputTokens);
        if (Equals(window, usage.Window))
        {
            return usage;
        }

        return usage with
        {
            Window = window,
            Scope = usage.Scope is AgentUsageScope.Unknown && currentTokens is not null
                ? AgentUsageScope.CurrentWindow
                : usage.Scope,
        };
    }

    // 函数功能：将外部估算的上下文窗口用量（token 数和消息数）附加到用量快照；数据不足时原样返回
    public static AgentSessionUsage? AttachWindowEstimate(
        AgentSessionUsage? usage,
        AgentModelInfo? modelInfo,
        long? currentTokens,
        int? messageCount,
        DateTimeOffset updatedAt,
        string? label = null)
    {
        if (messageCount is not >= 0 && currentTokens is null)
        {
            return usage;
        }

        var budget = GetModelTokenBudget(modelInfo);
        var tokenLimit = usage?.Window?.TokenLimit ?? budget.InputContextLimit;
        if (currentTokens is null && tokenLimit is null)
        {
            return usage;
        }

        var resolvedLabel = string.IsNullOrWhiteSpace(label)
            ? "Estimated active context"
            : label;
        var window = new AgentWindowUsageSnapshot(
            CurrentTokens: currentTokens,
            TokenLimit: tokenLimit,
            MessageCount: messageCount,
            Label: resolvedLabel,
            TotalContextEnvelope: usage?.Window?.TotalContextEnvelope ?? budget.TotalContextEnvelope,
            MaxOutputTokens: usage?.Window?.MaxOutputTokens ?? budget.MaxOutputTokens);
        if (usage is not null &&
            Equals(window, usage.Window) &&
            usage.Scope == AgentUsageScope.CurrentWindow)
        {
            return usage;
        }

        return (usage ?? new AgentSessionUsage()) with
        {
            Window = window,
            Scope = AgentUsageScope.CurrentWindow,
            Source = usage?.Source is AgentUsageSource.Unknown or null
                ? AgentUsageSource.ProviderUsage
                : usage.Source,
            UpdatedAt = updatedAt,
        };
    }

    // 函数功能：根据模型信息解析 token 预算（输入上下文限制、总上下文包络、最大输出）
    private static AgentTokenBudget GetModelTokenBudget(AgentModelInfo? modelInfo)
    {
        return AgentTokenBudgetResolver.Resolve(modelInfo, AgentCompactionSettings.Default);
    }

    // 函数功能：可空长整数求和，两者均为 null 时返回 null
    private static long? Sum(long? left, long? right)
        => left.HasValue || right.HasValue ? (left ?? 0) + (right ?? 0) : null;

    // 函数功能：将新用量快照合并到当前用量，incoming 字段优先，current 为 null 时直接返回 incoming
    private static AgentSessionUsage MergeUsage(AgentSessionUsage? current, AgentSessionUsage incoming)
    {
        if (current is null)
        {
            return incoming;
        }

        return new AgentSessionUsage(
            Window: MergeWindowUsage(current.Window, incoming.Window),
            LastOperation: MergeOperationUsage(current.LastOperation, incoming.LastOperation),
            RateLimits: incoming.RateLimits ?? current.RateLimits,
            Scope: incoming.Scope,
            Source: incoming.Source,
            UpdatedAt: incoming.UpdatedAt,
            Details: incoming.Details ?? current.Details);
    }

    // 函数功能：合并窗口用量快照，incoming 各字段非 null 时覆盖 current 对应字段
    private static AgentWindowUsageSnapshot? MergeWindowUsage(AgentWindowUsageSnapshot? current, AgentWindowUsageSnapshot? incoming)
    {
        if (incoming is null)
        {
            return current;
        }

        if (current is null)
        {
            return incoming;
        }

        return current with
        {
            CurrentTokens = incoming.CurrentTokens ?? current.CurrentTokens,
            TokenLimit = incoming.TokenLimit ?? current.TokenLimit,
            MessageCount = incoming.MessageCount ?? current.MessageCount,
            Label = incoming.Label ?? current.Label,
            TotalContextEnvelope = incoming.TotalContextEnvelope ?? current.TotalContextEnvelope,
            MaxOutputTokens = incoming.MaxOutputTokens ?? current.MaxOutputTokens,
        };
    }

    // 函数功能：合并单次操作用量快照，incoming 各字段非 null 时覆盖 current 对应字段
    private static AgentOperationUsageSnapshot? MergeOperationUsage(AgentOperationUsageSnapshot? current, AgentOperationUsageSnapshot? incoming)
    {
        if (incoming is null)
        {
            return current;
        }

        if (current is null)
        {
            return incoming;
        }

        return current with
        {
            Model = incoming.Model ?? current.Model,
            InputTokens = incoming.InputTokens ?? current.InputTokens,
            OutputTokens = incoming.OutputTokens ?? current.OutputTokens,
            CacheReadTokens = incoming.CacheReadTokens ?? current.CacheReadTokens,
            CacheWriteTokens = incoming.CacheWriteTokens ?? current.CacheWriteTokens,
            CachedInputTokens = incoming.CachedInputTokens ?? current.CachedInputTokens,
            ReasoningTokens = incoming.ReasoningTokens ?? current.ReasoningTokens,
            Cost = incoming.Cost ?? current.Cost,
            DurationMs = incoming.DurationMs ?? current.DurationMs,
            Initiator = incoming.Initiator ?? current.Initiator,
            ParentToolCallId = incoming.ParentToolCallId ?? current.ParentToolCallId,
            ReasoningEffort = incoming.ReasoningEffort ?? current.ReasoningEffort,
            Label = incoming.Label ?? current.Label,
        };
    }

}
