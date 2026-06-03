namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：对话压缩规划器，根据 token 预算计算哪些消息需摘要、哪些需保留
internal static class AgentCompactionPlanner
{
    // 说明：默认允许对超大锚点消息执行缩减
    private const bool DefaultReduceOversizedAnchors = true;

    // 函数功能：根据触发源、会话消息、token 预算和设置，生成压缩准备计划；无需压缩时返回 null
    public static AgentCompactionPreparation? Prepare(
        AgentCompactionTrigger trigger,
        string? systemMessage,
        string? developerInstructions,
        IReadOnlyList<AgentConversationMessage> conversation,
        AgentSessionUsage? usage,
        AgentTokenBudget budget,
        AgentCompactionSettings settings,
        string? anchorContentId = null,
        long? checkpointTokenEstimate = null,
        long? promptBudgetOverride = null,
        bool keepAnchorOnly = false,
        bool allowOversizedAnchorReduction = DefaultReduceOversizedAnchors)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(settings);

        var previousSummary = ExtractLeadingCheckpointSummary(conversation, out var startIndex);
        var effectiveConversation = conversation.Skip(startIndex).ToArray();
        if (effectiveConversation.Length == 0)
        {
            return null;
        }

        var tokensBefore = AgentTokenEstimator.EstimatePromptTokens(
            systemMessage,
            developerInstructions,
            conversation,
            usage);
        if (effectiveConversation.Length < 2)
        {
            return null;
        }

        var groups = BuildGroups(effectiveConversation);
        var checkpointTokens = checkpointTokenEstimate
            ?? (previousSummary is null
                ? 64L
                : Math.Max(AgentTokenEstimator.EstimateCheckpointTokens(previousSummary), 64L));
        var promptBudget = ResolveRetainedPromptBudget(tokensBefore.Tokens, budget.InputContextLimit, settings, promptBudgetOverride);
        var fixedTokenCost = AgentTokenEstimator.EstimatePromptTokens(
            systemMessage,
            developerInstructions,
            [],
            usage: null).Tokens;
        var availableForRetained = Math.Max(promptBudget - fixedTokenCost - checkpointTokens, 0L);
        var anchorGroupIndex = settings.KeepLastUserMessage ? FindLatestUserGroupIndex(groups) : null;

        IReadOnlyList<int> turnPrefixIndexes = [];
        IReadOnlyList<int> suffixIndexes;
        bool isSplitTurn;
        int? oversizedAnchorGroupIndex = null;

        if (keepAnchorOnly)
        {
            (turnPrefixIndexes, suffixIndexes, isSplitTurn, oversizedAnchorGroupIndex) = BuildAnchorOnlyPlan(
                groups,
                anchorGroupIndex,
                availableForRetained,
                allowOversizedAnchorReduction);
        }
        else
        {
            (turnPrefixIndexes, suffixIndexes, isSplitTurn, oversizedAnchorGroupIndex) = BuildPlan(
                groups,
                anchorGroupIndex,
                settings.AllowSplitTurn,
                availableForRetained,
                allowOversizedAnchorReduction);
        }

        var retainedIndexes = turnPrefixIndexes.Concat(suffixIndexes).ToHashSet();
        if (oversizedAnchorGroupIndex is { } excludedAnchorIndex)
        {
            retainedIndexes.Add(excludedAnchorIndex);
        }

        var messagesToSummarize = FlattenGroups(
            groups,
            Enumerable.Range(0, groups.Count).Where(index => !retainedIndexes.Contains(index)));
        if (messagesToSummarize.Count == 0 && oversizedAnchorGroupIndex is null)
        {
            return null;
        }

        var oversizedAnchorMessage = oversizedAnchorGroupIndex is { } oversizedIndex
            ? groups[oversizedIndex].Messages.LastOrDefault(static message => message.Role is AgentConversationRole.User)
            : null;

        return new AgentCompactionPreparation(
            Trigger: trigger,
            MessagesToSummarize: messagesToSummarize,
            TurnPrefixMessages: FlattenGroups(groups, turnPrefixIndexes),
            MessagesToKeep: FlattenGroups(groups, suffixIndexes),
            AnchorContentId: anchorContentId,
            IsSplitTurn: isSplitTurn,
            TokensBefore: tokensBefore,
            PreviousSummary: previousSummary,
            OversizedAnchorMessage: oversizedAnchorMessage);
    }

    // 函数功能：计算压缩后保留消息的目标 token 上限，优先使用覆盖值，其次按上下文限制和比率推算
    private static long ResolveRetainedPromptBudget(
        long tokensBefore,
        long? inputContextLimit,
        AgentCompactionSettings settings,
        long? promptBudgetOverride)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (promptBudgetOverride is > 0)
        {
            if (inputContextLimit is > 0)
            {
                return Math.Max(Math.Min(promptBudgetOverride.Value, inputContextLimit.Value), 1L);
            }

            return promptBudgetOverride.Value;
        }

        var resolvedPromptBudget = inputContextLimit is > 0
            ? ResolveRetainedPromptBudget(inputContextLimit.Value, settings)
            : Math.Max(tokensBefore / 2, 1L);
        return Math.Max(resolvedPromptBudget, 1L);
    }

    // 函数功能：按上下文限制与配置比率计算保留预算
    private static long ResolveRetainedPromptBudget(long inputContextLimit, AgentCompactionSettings settings)
    {
        var ratio = settings.PostCompactionTargetRatio > 0
            ? Math.Min(settings.PostCompactionTargetRatio, AgentCompactionSettings.MaxPostCompactionTargetRatio)
            : AgentCompactionSettings.DefaultPostCompactionTargetRatio;
        return Math.Max((long)Math.Floor(inputContextLimit * ratio), 1L);
    }

    // 函数功能：构建完整压缩计划，决定锚点前缀组、后缀组及是否跨轮次分割，超大锚点时可缩减
    private static (IReadOnlyList<int> TurnPrefixIndexes, IReadOnlyList<int> SuffixIndexes, bool IsSplitTurn, int? OversizedAnchorGroupIndex) BuildPlan(
        IReadOnlyList<MessageGroup> groups,
        int? anchorGroupIndex,
        bool allowSplitTurn,
        long availableForRetained,
        bool allowOversizedAnchorReduction)
    {
        if (groups.Count == 0)
        {
            return ([], [], false, null);
        }

        if (anchorGroupIndex is null)
        {
            return ([], groups[^1].Tokens <= availableForRetained ? [groups.Count - 1] : [], false, null);
        }

        var anchoredSuffixIndexes = BuildSuffixFromStart(groups, anchorGroupIndex.Value);
        var anchoredSuffixTokens = SumTokens(groups, anchoredSuffixIndexes);
        if (anchoredSuffixTokens <= availableForRetained)
        {
            return ([], anchoredSuffixIndexes, false, null);
        }

        if (!allowSplitTurn)
        {
            throw new InvalidOperationException("The latest user turn does not fit within the resolved prompt budget and split-turn compaction is disabled.");
        }

        var anchorTokens = groups[anchorGroupIndex.Value].Tokens;
        if (anchorTokens > availableForRetained)
        {
            if (allowOversizedAnchorReduction)
            {
                var reducedSuffixIndexes = BuildContiguousSuffix(groups, availableForRetained, minimumStartIndexExclusive: anchorGroupIndex.Value);
                return ([], reducedSuffixIndexes, true, anchorGroupIndex.Value);
            }

            throw new InvalidOperationException("The latest user message is too large to keep within the resolved prompt budget.");
        }

        var remainingBudget = availableForRetained - anchorTokens;
        var suffixIndexes = BuildContiguousSuffix(groups, remainingBudget, minimumStartIndexExclusive: anchorGroupIndex.Value);
        var isSplitTurn = suffixIndexes.Count > 0 || anchorGroupIndex.Value < groups.Count - 1;
        return ([anchorGroupIndex.Value], suffixIndexes, isSplitTurn, null);
    }

    // 函数功能：构建仅保留锚点的压缩计划，不保留后续消息组
    private static (IReadOnlyList<int> TurnPrefixIndexes, IReadOnlyList<int> SuffixIndexes, bool IsSplitTurn, int? OversizedAnchorGroupIndex) BuildAnchorOnlyPlan(
        IReadOnlyList<MessageGroup> groups,
        int? anchorGroupIndex,
        long availableForRetained,
        bool allowOversizedAnchorReduction)
    {
        if (anchorGroupIndex is null)
        {
            return ([], [], false, null);
        }

        if (groups[anchorGroupIndex.Value].Tokens > availableForRetained)
        {
            if (allowOversizedAnchorReduction)
            {
                return ([], [], anchorGroupIndex.Value < groups.Count - 1, anchorGroupIndex.Value);
            }

            throw new InvalidOperationException("The latest user message is too large to keep within the resolved prompt budget.");
        }

        return ([anchorGroupIndex.Value], [], anchorGroupIndex.Value < groups.Count - 1, null);
    }

    // 函数功能：从尾部向前贪心选取连续消息组，使总 token 不超出预算，返回组索引列表
    private static IReadOnlyList<int> BuildContiguousSuffix(
        IReadOnlyList<MessageGroup> groups,
        long availableForRetained,
        int? maximumStartIndex = null,
        int? minimumStartIndexExclusive = null)
    {
        if (availableForRetained <= 0)
        {
            return [];
        }

        var startLimit = maximumStartIndex ?? groups.Count - 1;
        var minimumAllowedStart = (minimumStartIndexExclusive ?? -1) + 1;
        var keepTokens = 0L;
        var suffixIndexes = new Stack<int>();
        for (var index = groups.Count - 1; index >= minimumAllowedStart; index--)
        {
            var candidateTokens = keepTokens + groups[index].Tokens;
            if (candidateTokens > availableForRetained)
            {
                if (suffixIndexes.Count == 0 && index == groups.Count - 1 && index <= startLimit && groups[index].Tokens <= availableForRetained)
                {
                    suffixIndexes.Push(index);
                }

                break;
            }

            suffixIndexes.Push(index);
            keepTokens = candidateTokens;
            if (index == 0 || index - 1 < minimumAllowedStart)
            {
                break;
            }

            if (index - 1 < 0)
            {
                break;
            }
        }

        var suffix = suffixIndexes.ToList();
        if (suffix.Count == 0)
        {
            return [];
        }

        while (suffix.Count > 0 && suffix[0] > startLimit)
        {
            suffix.RemoveAt(0);
        }

        return suffix;
    }

    // 函数功能：从 startIndex 到末尾生成连续组索引列表
    private static IReadOnlyList<int> BuildSuffixFromStart(IReadOnlyList<MessageGroup> groups, int startIndex)
        => Enumerable.Range(startIndex, groups.Count - startIndex).ToArray();

    // 函数功能：若会话首条消息是压缩检查点，提取其摘要文本并输出有效消息起始索引
    private static string? ExtractLeadingCheckpointSummary(
        IReadOnlyList<AgentConversationMessage> conversation,
        out int startIndex)
    {
        if (conversation.Count > 0 &&
            AgentCompactionCheckpoint.TryExtractSummary(conversation[0]) is { } summary)
        {
            startIndex = 1;
            return summary;
        }

        startIndex = 0;
        return null;
    }

    // 函数功能：将会话消息列表按轮次分组，Tool 消息附加到前一组
    private static List<MessageGroup> BuildGroups(IReadOnlyList<AgentConversationMessage> conversation)
    {
        var groups = new List<MessageGroup>();
        foreach (var message in conversation)
        {
            if (message.Role is AgentConversationRole.Tool && groups.Count > 0)
            {
                groups[^1].Messages.Add(message);
                groups[^1].Tokens += AgentTokenEstimator.EstimateMessage(message);
                continue;
            }

            groups.Add(new MessageGroup(message, AgentTokenEstimator.EstimateMessage(message)));
        }

        return groups;
    }

    // 函数功能：从后向前查找最新含 User 角色消息的组索引，不存在时返回 null
    private static int? FindLatestUserGroupIndex(IReadOnlyList<MessageGroup> groups)
    {
        for (var index = groups.Count - 1; index >= 0; index--)
        {
            if (groups[index].Messages.Any(static message => message.Role is AgentConversationRole.User))
            {
                return index;
            }
        }

        return null;
    }

    // 函数功能：对指定组索引集合的 token 数求和
    private static long SumTokens(IReadOnlyList<MessageGroup> groups, IEnumerable<int> indexes)
        => indexes.Sum(index => groups[index].Tokens);

    // 函数功能：按给定索引顺序将多个消息组展平为单一消息列表
    private static IReadOnlyList<AgentConversationMessage> FlattenGroups(
        IReadOnlyList<MessageGroup> groups,
        IEnumerable<int> indexes)
    {
        var orderedIndexes = indexes.OrderBy(static index => index);
        var messages = new List<AgentConversationMessage>();
        foreach (var index in orderedIndexes)
        {
            messages.AddRange(groups[index].Messages);
        }

        return messages;
    }

    // 类型：消息组，将同一轮次的消息及其 token 估算值聚合在一起
    private sealed class MessageGroup
    {
        // 函数功能：用首条消息和其 token 数初始化消息组
        public MessageGroup(AgentConversationMessage message, long tokens)
        {
            Messages = [message];
            Tokens = tokens;
        }

        // 说明：本组包含的消息列表
        public List<AgentConversationMessage> Messages { get; }

        // 说明：本组的估算 token 总数（可累加）
        public long Tokens { get; set; }
    }
}
