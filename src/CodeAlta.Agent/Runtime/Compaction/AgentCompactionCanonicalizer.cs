using System.Text.Json;

namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：将对话消息序列规范化为压缩单元列表，合并工具调用交互并折叠重复的低价值操作
internal static class AgentCompactionCanonicalizer
{
    // 函数功能：将消息列表规范化为压缩单元序列，先构建单元再折叠重复低价值单元
    public static IReadOnlyList<AgentCompactionUnit> Normalize(IReadOnlyList<AgentConversationMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var units = BuildUnits(messages);
        if (units.Count <= 1)
        {
            return units;
        }

        return CollapseRepeatedLowValueUnits(units);
    }

    // 函数功能：将消息列表转换为压缩单元，Assistant 带工具调用时与紧随其后的工具结果消息合并为 ToolInteractionUnit
    private static IReadOnlyList<AgentCompactionUnit> BuildUnits(IReadOnlyList<AgentConversationMessage> messages)
    {
        var units = new List<AgentCompactionUnit>();
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            if (message.Role is AgentConversationRole.Assistant &&
                message.Parts.OfType<AgentMessagePart.ToolCall>().Any())
            {
                var toolMessages = new List<AgentConversationMessage>();
                var expectedCallIds = message.Parts
                    .OfType<AgentMessagePart.ToolCall>()
                    .Select(static part => part.CallId)
                    .ToHashSet(StringComparer.Ordinal);

                var nextIndex = index + 1;
                while (nextIndex < messages.Count &&
                       messages[nextIndex].Role is AgentConversationRole.Tool &&
                       messages[nextIndex].Parts.OfType<AgentMessagePart.ToolResult>().Any(part => expectedCallIds.Contains(part.CallId)))
                {
                    toolMessages.Add(messages[nextIndex]);
                    nextIndex++;
                }

                units.Add(new AgentCompactionToolInteractionUnit(message, toolMessages));
                index = nextIndex - 1;
                continue;
            }

            units.Add(new AgentCompactionMessageUnit(message));
        }

        return units;
    }

    // 函数功能：将连续出现的相同低价值工具交互单元折叠为一个带重复计数的单元
    private static IReadOnlyList<AgentCompactionUnit> CollapseRepeatedLowValueUnits(IReadOnlyList<AgentCompactionUnit> units)
    {
        var normalized = new List<AgentCompactionUnit>(units.Count);
        for (var index = 0; index < units.Count; index++)
        {
            if (units[index] is not AgentCompactionToolInteractionUnit candidate ||
                !TryGetLowValueCollapseKey(candidate, out var collapseKey))
            {
                normalized.Add(units[index]);
                continue;
            }

            var run = new List<AgentCompactionToolInteractionUnit> { candidate };
            var nextIndex = index + 1;
            while (nextIndex < units.Count &&
                   units[nextIndex] is AgentCompactionToolInteractionUnit nextCandidate &&
                   TryGetLowValueCollapseKey(nextCandidate, out var nextKey) &&
                   string.Equals(collapseKey, nextKey, StringComparison.Ordinal))
            {
                run.Add(nextCandidate);
                nextIndex++;
            }

            if (run.Count == 1)
            {
                normalized.Add(candidate);
                continue;
            }

            var latest = run[^1];
            normalized.Add(new AgentCompactionToolInteractionUnit(
                latest.AssistantMessage,
                latest.ToolMessages,
                RepeatCount: run.Count,
                IsCollapsed: true,
                CollapseKey: collapseKey));
            index = nextIndex - 1;
        }

        return normalized;
    }

    // 函数功能：判断单个工具交互单元是否为低价值可折叠单元，是则输出折叠键（工具名+参数签名）
    private static bool TryGetLowValueCollapseKey(AgentCompactionToolInteractionUnit unit, out string key)
    {
        key = string.Empty;
        if (unit.ToolCalls.Count != 1 ||
            unit.ToolResults.Count != 1 ||
            unit.ToolMessages.Count != 1 ||
            unit.RepeatCount != 1)
        {
            return false;
        }

        if (!unit.ToolResults[0].Result.Success ||
            HasHighSignalToolOutput(unit.ToolResults[0].Result))
        {
            return false;
        }

        if (unit.AssistantMessage.Parts.Any(static part => part is not AgentMessagePart.ToolCall))
        {
            return false;
        }

        var toolCall = unit.ToolCalls[0];
        var signature = TryGetToolSignature(toolCall.Name, toolCall.Arguments);
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        key = signature;
        return true;
    }

    // 函数功能：判断工具结果是否含高信号内容（含错误或关键词如 error/fail/timeout 等）
    private static bool HasHighSignalToolOutput(AgentToolResult result)
    {
        var rendered = string.Join(
            "\n",
            result.Items.OfType<AgentToolResultItem.Text>().Select(static item => item.Value));
        return !string.IsNullOrWhiteSpace(result.Error) ||
               ContainsHighSignal(rendered);
    }

    // 函数功能：根据工具名称和参数生成可用于折叠比较的规范化签名字符串，不支持的工具返回 null
    private static string? TryGetToolSignature(string toolName, JsonElement arguments)
    {
        static string? GetString(JsonElement args, string propertyName)
            => args.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.String
                ? property.GetString()
                : null;

        var normalized = toolName switch
        {
            "read_file" or "list_dir" => GetString(arguments, "path"),
            "grep" => $"{GetString(arguments, "path")}|{GetString(arguments, "pattern") ?? GetString(arguments, "query")}",
            "shell_command" => GetString(arguments, "command"),
            "write_file" or "replace_in_file" or "delete_file_or_dir" => GetString(arguments, "path"),
            "rename_file_or_dir" => $"{GetString(arguments, "old_path")}|{GetString(arguments, "new_path")}",
            "webget" => GetString(arguments, "url"),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return $"{toolName}|{normalized.Trim()}";
    }

    // 函数功能：检测文本中是否包含预定义的高信号关键词（不区分大小写）
    private static bool ContainsHighSignal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] keywords =
        [
            "error",
            "exception",
            "fail",
            "failed",
            "traceback",
            "fatal",
            "warning",
            "assert",
            "timed out",
            "timeout",
        ];

        return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
