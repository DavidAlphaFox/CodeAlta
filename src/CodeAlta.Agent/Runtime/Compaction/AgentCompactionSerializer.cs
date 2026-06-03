using System.Security;
using System.Text;
using System.Text.Json;

namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：对话压缩序列化器，将会话消息单元序列化为结构化 XML 请求体，并管理工具调用结果与推理内容的摘录分配
internal static class AgentCompactionSerializer
{
    private const int ToolExcerptPerItemCharacterLimit = 1_200;
    private const int ToolExcerptTotalCharacterLimit = 6_000;
    private const int ReasoningExcerptPerItemCharacterLimit = 600;
    private const int ReasoningExcerptTotalCharacterLimit = 3_000;
    private const AgentCompactionReasoningMode ReasoningMode = AgentCompactionReasoningMode.Adaptive;

    private static readonly string[] HighSignalKeywords =
    [
        "error",
        "exception",
        "fail",
        "failed",
        "traceback",
        "fatal",
        "warning",
        "test",
        "build",
        "assert",
        "denied",
        "timed out",
        "timeout",
    ];

    // 函数功能：构建摘要请求的完整 XML 请求体，包括会话消息、前后缀、文件活动及统计信息，返回序列化结果
    public static AgentCompactionSerializationResult BuildSummaryRequestBody(
        AgentCompactionPreparation preparation,
        string? latestUserRequest,
        IReadOnlyList<string> readFiles,
        IReadOnlyList<string> modifiedFiles,
        AgentCompactionSettings settings,
        string? oversizedAnchorSynopsis = null,
        bool oversizedAnchorReduced = false)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(readFiles);
        ArgumentNullException.ThrowIfNull(modifiedFiles);
        ArgumentNullException.ThrowIfNull(settings);

        var summarizedUnits = AgentCompactionCanonicalizer.Normalize(preparation.MessagesToSummarize);
        var retainedPrefixUnits = AgentCompactionCanonicalizer.Normalize(preparation.TurnPrefixMessages);
        var retainedSuffixUnits = AgentCompactionCanonicalizer.Normalize(preparation.MessagesToKeep);

        var state = new SerializationState(oversizedAnchorReduced);
        AllocateExpensiveParts(summarizedUnits, retainedPrefixUnits, retainedSuffixUnits, state);

        var builder = new StringBuilder();
        builder.AppendLine("""<codealta-compaction-request version="2">""");
        AppendTag(builder, "mode", preparation.PreviousSummary is null ? "initial" : "update");
        AppendTag(builder, "trigger", preparation.Trigger.ToString().ToLowerInvariant());
        AppendTag(builder, "split-turn", preparation.IsSplitTurn ? "true" : "false");
        AppendTag(
            builder,
            "active-user-request",
            string.IsNullOrWhiteSpace(oversizedAnchorSynopsis) ? latestUserRequest?.Trim() : oversizedAnchorSynopsis.Trim());

        if (!string.IsNullOrWhiteSpace(oversizedAnchorSynopsis))
        {
            AppendTag(builder, "oversized-anchor-synopsis", oversizedAnchorSynopsis);
        }

        if (!string.IsNullOrWhiteSpace(preparation.PreviousSummary))
        {
            AppendTag(builder, "previous-summary", preparation.PreviousSummary);
        }

        AppendTag(builder, "conversation", SerializeUnits(summarizedUnits, state));

        if (retainedPrefixUnits.Count > 0)
        {
            AppendTag(builder, "retained-prefix", SerializeUnits(retainedPrefixUnits, state));
        }

        if (retainedSuffixUnits.Count > 0)
        {
            AppendTag(builder, "retained-suffix", SerializeUnits(retainedSuffixUnits, state));
        }

        AppendTag(builder, "relevant-files", RenderFileActivity(readFiles, modifiedFiles));
        builder.Append("""</codealta-compaction-request>""");

        var body = builder.ToString();
        var totalMessages = preparation.MessagesToSummarize.Count + preparation.TurnPrefixMessages.Count + preparation.MessagesToKeep.Count;
        var statistics = state.BuildStatistics();
        return new AgentCompactionSerializationResult(
            UserMessage: body,
            EstimatedInputTokens: AgentTokenEstimator.EstimateTextTokens(body),
            IncludedMessageCount: totalMessages - statistics.DroppedMessageCount,
            TotalMessageCount: totalMessages,
            Statistics: statistics);
    }

    // 函数功能：按优先级顺序为所有消息单元的工具调用结果和推理内容分配摘录字符预算
    private static void AllocateExpensiveParts(
        IReadOnlyList<AgentCompactionUnit> summarizedUnits,
        IReadOnlyList<AgentCompactionUnit> retainedPrefixUnits,
        IReadOnlyList<AgentCompactionUnit> retainedSuffixUnits,
        SerializationState state)
    {
        var rankedUnits = new List<RankedUnit>(summarizedUnits.Count + retainedPrefixUnits.Count + retainedSuffixUnits.Count);
        var recency = 0;
        AddRankedUnits(rankedUnits, summarizedUnits, SectionRank.Summarized, ref recency);
        AddRankedUnits(rankedUnits, retainedPrefixUnits, SectionRank.RetainedPrefix, ref recency);
        AddRankedUnits(rankedUnits, retainedSuffixUnits, SectionRank.RetainedSuffix, ref recency);

        foreach (var rankedUnit in OrderRankedUnits(rankedUnits))
        {
            if (rankedUnit.Unit is AgentCompactionToolInteractionUnit { IsCollapsed: true })
            {
                continue;
            }

            foreach (var message in rankedUnit.Unit.SourceMessages)
            {
                for (var partIndex = 0; partIndex < message.Parts.Count; partIndex++)
                {
                    switch (message.Parts[partIndex])
                    {
                        case AgentMessagePart.ToolResult toolResult:
                            AllocateToolResult(message, partIndex, toolResult, state);
                            break;
                        case AgentMessagePart.Reasoning reasoning:
                            AllocateReasoning(message, partIndex, reasoning, state);
                            break;
                        case AgentMessagePart.Data:
                            state.OmittedAttachmentCount++;
                            break;
                    }
                }
            }
        }
    }

    // 函数功能：将一批消息单元附加优先级和时序信息后加入排名列表
    private static void AddRankedUnits(
        ICollection<RankedUnit> target,
        IReadOnlyList<AgentCompactionUnit> units,
        SectionRank sectionRank,
        ref int recency)
    {
        foreach (var unit in units)
        {
            target.Add(new RankedUnit(unit, ComputePriority(unit, sectionRank), recency++));
        }
    }

    // 函数功能：对排名单元按优先级降序排列，同优先级时含工具结果的单元按时序优先
    private static IOrderedEnumerable<RankedUnit> OrderRankedUnits(IEnumerable<RankedUnit> rankedUnits)
    {
        ArgumentNullException.ThrowIfNull(rankedUnits);

        return rankedUnits
            .OrderByDescending(static item => item.Priority)
            .ThenByDescending(static item => item.ContainsToolResult ? item.Recency : int.MinValue)
            .ThenByDescending(static item => item.Recency);
    }

    // 函数功能：根据所属区段（保留后缀>保留前缀>待摘要）、角色（User>Assistant>Tool）及工具失败标志计算排名分值
    private static int ComputePriority(AgentCompactionUnit unit, SectionRank sectionRank)
    {
        var sectionWeight = sectionRank switch
        {
            SectionRank.RetainedSuffix => 300,
            SectionRank.RetainedPrefix => 200,
            _ => 100,
        };

        var roleWeight = unit.Role switch
        {
            AgentConversationRole.User => 40,
            AgentConversationRole.Assistant => 30,
            AgentConversationRole.Tool => 20,
            _ => 10,
        };

        var failureWeight = unit.SourceMessages
            .SelectMany(static message => message.Parts.OfType<AgentMessagePart.ToolResult>())
            .Any(static part => !part.Result.Success || !string.IsNullOrWhiteSpace(part.Result.Error))
            ? 25
            : 0;
        return sectionWeight + roleWeight + failureWeight;
    }

    // 函数功能：为单条工具调用结果分配摘录字符预算，将截断后的摘录存入序列化状态
    private static void AllocateToolResult(AgentConversationMessage message, int partIndex, AgentMessagePart.ToolResult toolResult, SerializationState state)
    {
        if (ToolExcerptPerItemCharacterLimit <= 0 || state.RemainingToolCharacters <= 0)
        {
            state.OmittedToolResultCount++;
            return;
        }

        var rendered = RenderToolResult(toolResult.Result);
        var excerpt = CreateToolExcerpt(rendered, ToolExcerptPerItemCharacterLimit);
        if (string.IsNullOrWhiteSpace(excerpt))
        {
            state.OmittedToolResultCount++;
            return;
        }

        var allocated = TrimToLength(excerpt, state.RemainingToolCharacters);
        if (string.IsNullOrWhiteSpace(allocated))
        {
            state.OmittedToolResultCount++;
            return;
        }

        state.SetToolResultExcerpt(message, partIndex, allocated);
        state.SerializedToolResultCharacters += allocated.Length;
        state.RemainingToolCharacters = Math.Max(state.RemainingToolCharacters - allocated.Length, 0);
        if (allocated.Length < excerpt.Length)
        {
            state.OmittedToolResultCount++;
        }
    }

    // 函数功能：为推理内容分配摘录预算，跳过受保护或空值推理；将截断摘录存入序列化状态
    private static void AllocateReasoning(AgentConversationMessage message, int partIndex, AgentMessagePart.Reasoning reasoning, SerializationState state)
    {
        if (!string.IsNullOrWhiteSpace(reasoning.ProtectedData) ||
            string.IsNullOrWhiteSpace(reasoning.Value))
        {
            if (!string.IsNullOrWhiteSpace(reasoning.Value) || !string.IsNullOrWhiteSpace(reasoning.ProtectedData))
            {
                state.OmittedReasoningCount++;
            }

            return;
        }

        if (ReasoningExcerptPerItemCharacterLimit <= 0 || state.RemainingReasoningCharacters <= 0)
        {
            state.OmittedReasoningCount++;
            return;
        }

        var excerpt = CreateReasoningExcerpt(reasoning.Value!, ReasoningExcerptPerItemCharacterLimit, ReasoningMode);
        if (string.IsNullOrWhiteSpace(excerpt))
        {
            state.OmittedReasoningCount++;
            return;
        }

        var allocated = TrimToLength(excerpt, state.RemainingReasoningCharacters);
        if (string.IsNullOrWhiteSpace(allocated))
        {
            state.OmittedReasoningCount++;
            return;
        }

        state.SetReasoningExcerpt(message, partIndex, allocated);
        state.SerializedReasoningCharacters += allocated.Length;
        state.RemainingReasoningCharacters = Math.Max(state.RemainingReasoningCharacters - allocated.Length, 0);
        if (allocated.Length < excerpt.Length)
        {
            state.OmittedReasoningCount++;
        }
    }

    // 函数功能：将消息单元列表序列化为多行文本，各单元之间以换行分隔
    private static string SerializeUnits(IReadOnlyList<AgentCompactionUnit> units, SerializationState state)
    {
        if (units.Count == 0)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
        {
            foreach (var line in SerializeUnit(units[unitIndex], state))
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().Trim();
    }

    // 函数功能：根据消息单元类型（普通消息、折叠工具交互、完整工具交互）分发序列化逻辑
    private static IEnumerable<string> SerializeUnit(AgentCompactionUnit unit, SerializationState state)
    {
        switch (unit)
        {
            case AgentCompactionMessageUnit messageUnit:
                foreach (var line in SerializeMessage(messageUnit.Message, state))
                {
                    yield return line;
                }

                break;
            case AgentCompactionToolInteractionUnit { IsCollapsed: true } collapsedUnit:
                foreach (var line in SerializeCollapsedToolInteraction(collapsedUnit, state))
                {
                    yield return line;
                }

                break;
            case AgentCompactionToolInteractionUnit toolInteractionUnit:
                foreach (var line in SerializeMessage(toolInteractionUnit.AssistantMessage, state))
                {
                    yield return line;
                }

                foreach (var toolMessage in toolInteractionUnit.ToolMessages)
                {
                    foreach (var line in SerializeMessage(toolMessage, state))
                    {
                        yield return line;
                    }
                }

                break;
        }
    }

    // 函数功能：将单条消息的各部分（文本、推理、工具调用/结果、附件）序列化为带角色标签的文本行
    private static IEnumerable<string> SerializeMessage(AgentConversationMessage message, SerializationState state)
    {
        var emitted = false;
        for (var partIndex = 0; partIndex < message.Parts.Count; partIndex++)
        {
            switch (message.Parts[partIndex])
            {
                case AgentMessagePart.Text text when !string.IsNullOrWhiteSpace(text.Value):
                    emitted = true;
                    yield return $"[{GetRoleLabel(message.Role)}] {text.Value.Trim()}";
                    break;
                case AgentMessagePart.Reasoning:
                    state.TotalReasoningCount++;
                    if (state.TryGetReasoningExcerpt(message, partIndex, out var reasoningExcerpt))
                    {
                        state.SerializedReasoningCount++;
                        emitted = true;
                        yield return $"[Assistant reasoning summary] {reasoningExcerpt}";
                    }

                    break;
                case AgentMessagePart.ToolCall toolCall:
                    state.TotalToolCallCount++;
                    state.SerializedToolCallCount++;
                    emitted = true;
                    yield return $"[Assistant tool calls] {toolCall.Name} {SummarizeArguments(toolCall.Arguments)}";
                    break;
                case AgentMessagePart.ToolResult toolResult:
                    state.TotalToolResultCount++;
                    state.SerializedToolResultCount++;
                    emitted = true;
                    var descriptor = BuildToolDescriptor(toolResult);
                    if (state.TryGetToolResultExcerpt(message, partIndex, out var toolExcerpt))
                    {
                        state.SerializedToolResultExcerptCount++;
                        yield return $"[Tool result summary] {descriptor}; excerpt: {toolExcerpt}";
                    }
                    else
                    {
                        yield return $"[Tool result summary] {descriptor}; bulk output omitted";
                    }

                    break;
                case AgentMessagePart.Uri uri:
                    state.TotalAttachmentCount++;
                    state.SerializedAttachmentCount++;
                    emitted = true;
                    yield return $"[Attachment] {uri.Name ?? uri.MediaType ?? "uri"}: {uri.Value}";
                    break;
                case AgentMessagePart.Data data:
                    state.TotalAttachmentCount++;
                    emitted = true;
                    yield return $"[Attachment] inline {data.Name ?? data.MediaType}; base64 omitted ({data.Base64Data.Length} chars)";
                    break;
            }
        }

        if (!emitted)
        {
            state.DroppedMessageCount++;
        }
    }

    // 函数功能：序列化折叠的重复工具交互单元，合并为一行重复次数摘要以节省 Token
    private static IEnumerable<string> SerializeCollapsedToolInteraction(AgentCompactionToolInteractionUnit unit, SerializationState state)
    {
        var toolCall = unit.ToolCalls.Single();
        var toolResult = unit.ToolResults.Single();
        var descriptor = BuildToolDescriptor(toolResult);
        state.TotalToolCallCount += unit.RepeatCount;
        state.SerializedToolCallCount++;
        state.CollapsedToolCallCount += Math.Max(unit.RepeatCount - 1, 0);
        state.TotalToolResultCount += unit.RepeatCount;
        state.SerializedToolResultCount++;
        yield return $"[Assistant tool calls] {toolCall.Name} {SummarizeArguments(toolCall.Arguments)} repeated {unit.RepeatCount} times";
        yield return $"[Tool result summary] repeated successful {toolCall.Name} activity ({unit.RepeatCount}x); latest {descriptor}; bulk output omitted";
    }

    // 函数功能：将对话角色枚举映射为可读的英文标签字符串
    private static string GetRoleLabel(AgentConversationRole role)
        => role switch
        {
            AgentConversationRole.User => "User",
            AgentConversationRole.Assistant => "Assistant",
            AgentConversationRole.Tool => "Tool result",
            AgentConversationRole.System => "System",
            _ => "Message",
        };

    // 函数功能：构建工具调用结果的简短描述，包含 callId、成功/失败状态及近似字符数
    private static string BuildToolDescriptor(AgentMessagePart.ToolResult toolResult)
    {
        var rendered = RenderToolResult(toolResult.Result);
        return $"callId={toolResult.CallId}, status={(toolResult.Result.Success ? "success" : "failed")}, approxChars={rendered.Length}";
    }

    // 函数功能：从工具调用结果文本中提取高信号行（含错误/异常等关键词）拼接为摘录，超长时截断
    private static string CreateToolExcerpt(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || maxLength <= 0)
        {
            return string.Empty;
        }

        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
        {
            return TrimToLength(text.Trim(), maxLength);
        }

        var highSignalLines = lines
            .Where(line => HighSignalKeywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();
        if (highSignalLines.Count == 0)
        {
            highSignalLines.AddRange(lines.Take(3));
            if (lines.Length > 3)
            {
                highSignalLines.Add(lines[^1]);
            }
        }
        else
        {
            foreach (var line in lines)
            {
                if (highSignalLines.Count >= 4)
                {
                    break;
                }

                if (!highSignalLines.Contains(line, StringComparer.Ordinal))
                {
                    highSignalLines.Add(line);
                }
            }
        }

        return TrimToLength(string.Join(" | ", highSignalLines), maxLength);
    }

    // 函数功能：将推理文本归一化为单行后按模式截取（SummaryOnly 仅取首句），超长时截断
    private static string CreateReasoningExcerpt(string text, int maxLength, AgentCompactionReasoningMode mode)
    {
        var normalized = string.Join(
            " ",
            text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (mode is AgentCompactionReasoningMode.SummaryOnly)
        {
            var sentence = normalized
                .Split(['.', '!', '?'], 2, StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            normalized = string.IsNullOrWhiteSpace(sentence) ? normalized : sentence;
        }

        return TrimToLength(normalized, maxLength);
    }

    // 函数功能：将工具调用参数 JSON 简化为可读摘要（最多 8 个属性），过长字符串值用占位符替换
    private static string SummarizeArguments(JsonElement arguments)
    {
        if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "{}";
        }

        if (arguments.ValueKind is not JsonValueKind.Object)
        {
            return TrimToLength(arguments.GetRawText(), 240);
        }

        var segments = new List<string>();
        var propertyCount = 0;
        foreach (var property in arguments.EnumerateObject())
        {
            propertyCount++;
            if (propertyCount > 8)
            {
                segments.Add("...");
                break;
            }

            segments.Add($"{property.Name}={SummarizeJsonValue(property.Name, property.Value)}");
        }

        return "{ " + string.Join(", ", segments) + " }";
    }

    // 函数功能：将 JSON 属性值按类型转换为简洁字符串表示，对象/数组折叠显示
    private static string SummarizeJsonValue(string propertyName, JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => SummarizeJsonString(propertyName, value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => $"[{value.GetArrayLength()} items]",
            JsonValueKind.Object => "{...}",
            _ => TrimToLength(value.GetRawText(), 120),
        };
    }

    // 函数功能：对 JSON 字符串值进行摘要化，input/patch 等大字段及超长值用字符数占位符替换
    private static string SummarizeJsonString(string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        if (string.Equals(propertyName, "input", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(propertyName, "patch", StringComparison.OrdinalIgnoreCase) ||
            value.Length > 200)
        {
            return QuoteJsonString($"(omitted, {value.Length} chars)");
        }

        return QuoteJsonString(value.Length <= 120 ? value : value[..117] + "...");
    }

    // 函数功能：将工具调用结果的各条目渲染为纯文本，无有效内容时返回错误信息或占位符
    private static string RenderToolResult(AgentToolResult result)
    {
        var segments = result.Items.Select(static item => item switch
        {
            AgentToolResultItem.Text text => text.Value,
            AgentToolResultItem.ImageUrl imageUrl => imageUrl.Url,
            _ => string.Empty,
        });
        var rendered = string.Join(Environment.NewLine, segments.Where(static value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(rendered) ? (result.Error ?? "(no output)") : rendered;
    }

    // 函数功能：将字符串截断至 maxLength 字符，超出时末尾加省略号
    private static string TrimToLength(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || maxLength <= 0)
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..Math.Max(maxLength - 3, 0)] + "...";
    }

    // 函数功能：对字符串进行 JSON 转义并加双引号包装，用于摘要参数输出
    private static string QuoteJsonString(string value)
        => "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            + "\"";

    // 函数功能：向 StringBuilder 追加一个 XML 标签块，值经过 XML 转义处理
    private static void AppendTag(StringBuilder builder, string tagName, string? value)
    {
        builder.Append('<').Append(tagName).AppendLine(">");
        builder.AppendLine(SecurityElement.Escape(value ?? string.Empty) ?? string.Empty);
        builder.Append("</").Append(tagName).AppendLine(">");
    }

    // 函数功能：将已读取和已修改的文件路径渲染为 Markdown 列表，分组显示（Modified/Read）
    private static string RenderFileActivity(
        IReadOnlyList<string> readFiles,
        IReadOnlyList<string> modifiedFiles)
    {
        if (readFiles.Count == 0 && modifiedFiles.Count == 0)
        {
            return "- None tracked.";
        }

        var builder = new StringBuilder();
        if (modifiedFiles.Count > 0)
        {
            builder.AppendLine("### Modified");
            foreach (var path in modifiedFiles)
            {
                builder.Append("- ").AppendLine(path);
            }
        }

        if (readFiles.Count > 0)
        {
            builder.AppendLine("### Read");
            foreach (var path in readFiles)
            {
                builder.Append("- ").AppendLine(path);
            }
        }

        return builder.ToString().Trim();
    }

    // 类型：序列化过程的可变状态，持有摘录字典及各类计数，供分配与序列化阶段共享
    private sealed class SerializationState(bool reducedOversizedAnchor)
    {
        // 说明：工具调用结果的摘录索引，按消息实例和 partIndex 双层键存储
        public Dictionary<AgentConversationMessage, Dictionary<int, string>> ToolResultExcerpts { get; }
            = new(ReferenceEqualityComparer.Instance);

        // 说明：推理内容的摘录索引，按消息实例和 partIndex 双层键存储
        public Dictionary<AgentConversationMessage, Dictionary<int, string>> ReasoningExcerpts { get; }
            = new(ReferenceEqualityComparer.Instance);

        public int RemainingToolCharacters { get; set; } = Math.Max(ToolExcerptTotalCharacterLimit, 0);

        public int RemainingReasoningCharacters { get; set; } = Math.Max(ReasoningExcerptTotalCharacterLimit, 0);

        public int OmittedToolResultCount { get; set; }

        public int OmittedReasoningCount { get; set; }

        public int OmittedAttachmentCount { get; set; }

        public int DroppedMessageCount { get; set; }

        public int SerializedToolResultCharacters { get; set; }

        public int SerializedReasoningCharacters { get; set; }

        public bool ReducedOversizedAnchor { get; } = reducedOversizedAnchor;

        public int TotalToolCallCount { get; set; }

        public int SerializedToolCallCount { get; set; }

        public int CollapsedToolCallCount { get; set; }

        public int TotalToolResultCount { get; set; }

        public int SerializedToolResultCount { get; set; }

        public int SerializedToolResultExcerptCount { get; set; }

        public int TotalReasoningCount { get; set; }

        public int SerializedReasoningCount { get; set; }

        public int TotalAttachmentCount { get; set; }

        public int SerializedAttachmentCount { get; set; }

        // 函数功能：存储指定消息指定部分的工具调用结果摘录
        public void SetToolResultExcerpt(AgentConversationMessage message, int partIndex, string value)
        {
            if (!ToolResultExcerpts.TryGetValue(message, out var parts))
            {
                parts = [];
                ToolResultExcerpts[message] = parts;
            }

            parts[partIndex] = value;
        }

        // 函数功能：存储指定消息指定部分的推理内容摘录
        public void SetReasoningExcerpt(AgentConversationMessage message, int partIndex, string value)
        {
            if (!ReasoningExcerpts.TryGetValue(message, out var parts))
            {
                parts = [];
                ReasoningExcerpts[message] = parts;
            }

            parts[partIndex] = value;
        }

        // 函数功能：尝试取出指定消息指定部分的工具调用结果摘录，成功返回 true
        public bool TryGetToolResultExcerpt(AgentConversationMessage message, int partIndex, out string value)
            => TryGetExcerpt(ToolResultExcerpts, message, partIndex, out value);

        // 函数功能：尝试取出指定消息指定部分的推理内容摘录，成功返回 true
        public bool TryGetReasoningExcerpt(AgentConversationMessage message, int partIndex, out string value)
            => TryGetExcerpt(ReasoningExcerpts, message, partIndex, out value);

        // 函数功能：通用摘录读取逻辑，从双层字典中按消息引用和 partIndex 查找摘录
        private static bool TryGetExcerpt(
            IReadOnlyDictionary<AgentConversationMessage, Dictionary<int, string>> source,
            AgentConversationMessage message,
            int partIndex,
            out string value)
        {
            if (source.TryGetValue(message, out var parts) &&
                parts.TryGetValue(partIndex, out value!))
            {
                return true;
            }

            value = string.Empty;
            return false;
        }

        // 函数功能：将当前状态中的所有统计计数打包为不可变的 AgentCompactionSerializerStatistics 记录
        public AgentCompactionSerializerStatistics BuildStatistics()
            => new(
                OmittedToolResultCount,
                OmittedReasoningCount,
                OmittedAttachmentCount,
                DroppedMessageCount,
                SerializedToolResultCharacters,
                SerializedReasoningCharacters,
                ReducedOversizedAnchor,
                TotalToolCallCount,
                SerializedToolCallCount,
                CollapsedToolCallCount,
                TotalToolResultCount,
                SerializedToolResultCount,
                SerializedToolResultExcerptCount,
                TotalReasoningCount,
                SerializedReasoningCount,
                TotalAttachmentCount,
                SerializedAttachmentCount);
    }

    // 类型：携带优先级和时序信息的消息单元包装，用于摘录预算分配排序
    private readonly record struct RankedUnit(AgentCompactionUnit Unit, int Priority, int Recency)
    {
        public bool ContainsToolResult => Unit.SourceMessages.Any(static message => message.Parts.Any(static part => part is AgentMessagePart.ToolResult));
    }

    // 类型：消息所属区段的排名枚举，决定摘录分配的基础权重
    private enum SectionRank
    {
        Summarized,      // 待摘要区段
        RetainedPrefix,  // 保留前缀区段
        RetainedSuffix,  // 保留后缀区段（权重最高）
    }
}
