using System.Text;
using CodeAlta.Agent.Runtime;

namespace CodeAlta.Agent.ModelCatalog;

// 模块功能：提供模型 ID 的规范化、模糊匹配与等价比较能力，支持去除日期后缀和分隔符归一化
internal static class AgentModelIdentity
{
    // 函数功能：在模型列表中查找与给定 modelId 最匹配的条目，先精确匹配后模糊匹配
    public static AgentModelInfo? FindBestMatch(
        IReadOnlyList<AgentModelInfo> models,
        string? modelId)
    {
        if (models.Count == 0 || string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var normalizedModelId = modelId.Trim();
        var exact = models.FirstOrDefault(model =>
            string.Equals(model.Id, normalizedModelId, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var lookupKeys = GetLookupKeySet(normalizedModelId);
        if (lookupKeys.Count == 0)
        {
            return null;
        }

        return models.FirstOrDefault(model => Matches(model, lookupKeys));
    }

    // 函数功能：判断两个模型标识符在规范化后是否等价（忽略大小写、分隔符和日期后缀）
    public static bool AreEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftKeys = GetLookupKeySet(left);
        return leftKeys.Count > 0 && Matches(right, leftKeys);
    }

    // 函数功能：为一组值生成去重后的查找键集合（含原值、规范化值和去日期后缀变体）
    public static IEnumerable<string> GetLookupKeys(params string?[] values)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            AddLookupKeys(keys, value);
        }

        return keys;
    }

    // 函数功能：判断模型的 Id 或 DisplayName 是否与给定查找键集合匹配
    public static bool Matches(AgentModelInfo model, IReadOnlySet<string> lookupKeys)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(lookupKeys);

        return Matches(model.Id, lookupKeys) || Matches(model.DisplayName, lookupKeys);
    }

    // 函数功能：判断任意字符串值生成的查找键是否与给定键集合有交集
    public static bool Matches(string? value, IReadOnlySet<string> lookupKeys)
    {
        ArgumentNullException.ThrowIfNull(lookupKeys);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var key in GetLookupKeys(value))
        {
            if (lookupKeys.Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    // 函数功能：为一组值生成 HashSet 形式的查找键集合（忽略大小写）
    public static HashSet<string> GetLookupKeySet(params string?[] values)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            AddLookupKeys(keys, value);
        }

        return keys;
    }

    // 函数功能：向键集合中添加原值、规范化值以及去除日期后缀后的变体
    private static void AddLookupKeys(ISet<string> keys, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        keys.Add(trimmed);

        var normalized = NormalizeLookupKey(trimmed);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            keys.Add(normalized);
        }

        var withoutDateSuffix = StripDateSuffix(trimmed);
        if (withoutDateSuffix is null)
        {
            return;
        }

        keys.Add(withoutDateSuffix);
        var normalizedWithoutDate = NormalizeLookupKey(withoutDateSuffix);
        if (!string.IsNullOrWhiteSpace(normalizedWithoutDate))
        {
            keys.Add(normalizedWithoutDate);
        }
    }

    // 函数功能：尝试去除形如 "-YYYY-MM-DD" 的日期后缀，不符合格式则返回 null
    private static string? StripDateSuffix(string value)
    {
        const int DateSuffixLength = 11;
        if (value.Length <= DateSuffixLength || value[^DateSuffixLength] != '-')
        {
            return null;
        }

        var dateSlice = value.AsSpan(value.Length - 10);
        return IsIsoDate(dateSlice)
            ? value[..^DateSuffixLength]
            : null;
    }

    // 函数功能：验证给定 span 是否符合 ISO 8601 日期格式（YYYY-MM-DD）
    private static bool IsIsoDate(ReadOnlySpan<char> value)
    {
        return value.Length == 10 &&
               char.IsDigit(value[0]) &&
               char.IsDigit(value[1]) &&
               char.IsDigit(value[2]) &&
               char.IsDigit(value[3]) &&
               value[4] == '-' &&
               char.IsDigit(value[5]) &&
               char.IsDigit(value[6]) &&
               value[7] == '-' &&
               char.IsDigit(value[8]) &&
               char.IsDigit(value[9]);
    }

    // 函数功能：将标识符规范化为小写、以连字符分隔的形式，忽略其他特殊字符
    private static string NormalizeLookupKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '.')
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
                pendingSeparator = false;
            }
            else if (c is '-' or '_' or ' ' or '/' or ':')
            {
                pendingSeparator = builder.Length > 0;
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}
