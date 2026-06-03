using System.Text;

namespace CodeAlta.Agent;

// 模块功能：提供轻量级启发式 token 估算，综合 UTF-8 字节数和词法分析两种策略取最大值
/// <summary>
/// Provides a lightweight heuristic token estimator for prompts, generated text, code, logs, and structured payloads.
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    /// Estimates the number of model tokens represented by the specified text.
    /// </summary>
    /// <param name="text">The text to estimate.</param>
    /// <returns>The estimated token count, or zero when <paramref name="text" /> is <see langword="null" /> or empty.</returns>
    public static long Estimate(string? text)
        => string.IsNullOrEmpty(text) ? 0 : Estimate(text.AsSpan());

    /// <summary>
    /// Estimates the number of model tokens represented by the specified text.
    /// </summary>
    /// <param name="text">The text to estimate.</param>
    /// <returns>The estimated token count, or zero when <paramref name="text" /> is empty.</returns>
    public static long Estimate(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        var byteEstimate = (Encoding.UTF8.GetByteCount(text) + 3L) / 4L;
        var lexicalEstimate = EstimateLexical(text);
        var estimate = Math.Max(byteEstimate, lexicalEstimate);

        // Keep a small safety margin for larger structured/code-like payloads without
        // making short natural-language snippets obviously too pessimistic.
        return estimate >= 10 ? estimate + ((estimate + 9) / 10) : estimate;
    }

    // 函数功能：对文本进行词法扫描估算 token 数，分别处理空白、标识符、数字和符号
    private static long EstimateLexical(ReadOnlySpan<char> text)
    {
        var tokens = 0L;
        var index = 0;

        while (index < text.Length)
        {
            var c = text[index];

            if (char.IsWhiteSpace(c))
            {
                tokens += EstimateWhitespace(text, ref index);
                continue;
            }

            if (IsIdentifierStart(c))
            {
                var start = index++;
                while (index < text.Length && IsIdentifierPart(text[index]))
                {
                    index++;
                }

                tokens += EstimateIdentifier(text[start..index]);
                continue;
            }

            if (char.IsDigit(c))
            {
                var start = index++;
                while (index < text.Length && IsNumberPart(text[index]))
                {
                    index++;
                }

                tokens += Math.Max(1, (index - start + 2) / 3);
                continue;
            }

            tokens++;
            index++;
        }

        return tokens;
    }

    // 函数功能：逐字符累计空白 token：换行符贡献 1 个，制表符贡献 1 个，连续空格每 8 个贡献 1 个
    private static long EstimateWhitespace(ReadOnlySpan<char> text, ref int index)
    {
        var tokens = 0L;
        var spaces = 0;

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            var c = text[index++];
            switch (c)
            {
                case '\n':
                    tokens++;
                    if (spaces > 1)
                    {
                        tokens += (spaces + 7) / 8;
                    }

                    spaces = 0;
                    break;
                case '\r':
                    break;
                case '\t':
                    tokens++;
                    break;
                default:
                    spaces++;
                    break;
            }
        }

        if (spaces > 1)
        {
            tokens += (spaces + 7) / 8;
        }

        return tokens;
    }

    // 函数功能：按驼峰/下划线/连字符分段估算标识符 token 数，超长部分每 6 字符额外加 1
    private static long EstimateIdentifier(ReadOnlySpan<char> word)
    {
        if (word.IsEmpty)
        {
            return 0;
        }

        var parts = 1L;
        var currentPartLength = 0;

        for (var index = 0; index < word.Length; index++)
        {
            var c = word[index];
            if (c is '_' or '-')
            {
                parts++;
                currentPartLength = 0;
                continue;
            }

            if (index > 0)
            {
                var previous = word[index - 1];
                if (char.IsLower(previous) && char.IsUpper(c))
                {
                    parts++;
                    currentPartLength = 0;
                }
                else if (index + 1 < word.Length &&
                         char.IsUpper(previous) &&
                         char.IsUpper(c) &&
                         char.IsLower(word[index + 1]))
                {
                    parts++;
                    currentPartLength = 0;
                }
            }

            currentPartLength++;
            if (currentPartLength > 12 && currentPartLength % 6 == 0)
            {
                parts++;
            }
        }

        return Math.Max(1, parts);
    }

    private static bool IsIdentifierStart(char c)
        => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c)
        => char.IsLetterOrDigit(c) || c is '_' or '-';

    private static bool IsNumberPart(char c)
        => char.IsLetterOrDigit(c) || c is '.' or '_' or '-';
}
