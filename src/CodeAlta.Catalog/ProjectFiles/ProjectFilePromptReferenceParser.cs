using System.Buffers;
using CodeAlta.Catalog;

namespace CodeAlta.Catalog;

public static class ProjectFilePromptReferenceParser
{
    private static readonly SearchValues<char> ReferenceTerminators = SearchValues.Create(" \t\r\n,;!?)\u005D}>");
    private static readonly SearchValues<char> TrailingPunctuation = SearchValues.Create(".,;!?)\u005D}>");

    public static IReadOnlyList<ProjectFilePromptToken> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var tokens = new List<ProjectFilePromptToken>();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '[' &&
                TryParseMarkdownLinkReference(text, index, out var markdownLinkToken, out var markdownLinkLength))
            {
                tokens.Add(markdownLinkToken);
                index += markdownLinkLength - 1;
                continue;
            }

            if (text[index] != '@')
            {
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '@')
            {
                tokens.Add(new ProjectFilePromptToken(ProjectFilePromptTokenKind.EscapedAt, index, 2, "@@"));
                index++;
                continue;
            }

            if (!IsReferenceBoundary(text, index))
            {
                continue;
            }

            if (TryParseReference(text, index, out var token, out var consumedLength))
            {
                tokens.Add(token);
                index += consumedLength - 1;
            }
        }

        return tokens;
    }

    public static bool TryGetActiveReference(
        string? text,
        int caretIndex,
        out ProjectFilePromptActiveReference activeReference)
    {
        activeReference = default!;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        caretIndex = Math.Clamp(caretIndex, 0, text.Length);
        for (var index = caretIndex - 1; index >= 0; index--)
        {
            if (text[index] != '@')
            {
                continue;
            }

            if (index > 0 && text[index - 1] == '@')
            {
                continue;
            }

            if (!IsReferenceBoundary(text, index))
            {
                continue;
            }

            if (TryParseActiveReference(text, index, caretIndex, out activeReference))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseReference(
        string text,
        int atIndex,
        out ProjectFilePromptToken token,
        out int consumedLength)
    {
        var index = atIndex + 1;
        if (index >= text.Length)
        {
            token = default!;
            consumedLength = 0;
            return false;
        }

        string lookupText;
        var malformed = false;
        if (text[index] == '"')
        {
            var closingQuoteIndex = text.IndexOf('"', index + 1);
            if (closingQuoteIndex < 0)
            {
                lookupText = text[(index + 1)..];
                index = text.Length;
                malformed = true;
            }
            else
            {
                lookupText = text.Substring(index + 1, closingQuoteIndex - index - 1);
                index = closingQuoteIndex + 1;
            }
        }
        else
        {
            var pathStart = index;
            while (index < text.Length &&
                   !ReferenceTerminators.Contains(text[index]) &&
                   text[index] != ':')
            {
                index++;
            }

            if (index == pathStart)
            {
                token = default!;
                consumedLength = 0;
                return false;
            }

            lookupText = text.Substring(pathStart, index - pathStart);
            while (lookupText.Length > 0 && TrailingPunctuation.Contains(lookupText[^1]))
            {
                lookupText = lookupText[..^1];
                index--;
            }
        }

        if (lookupText.Length == 0)
        {
            token = default!;
            consumedLength = 0;
            return false;
        }

        var lineRange = ParseRange(text, ref index);
        consumedLength = index - atIndex;
        token = new ProjectFilePromptToken(
            ProjectFilePromptTokenKind.Reference,
            atIndex,
            consumedLength,
            text.Substring(atIndex, consumedLength),
            lookupText,
            lineRange,
            malformed);
        return true;
    }

    private static bool TryParseMarkdownLinkReference(
        string text,
        int openBracketIndex,
        out ProjectFilePromptToken token,
        out int consumedLength)
    {
        token = default!;
        consumedLength = 0;

        var closeBracketIndex = text.IndexOf(']', openBracketIndex + 1);
        if (closeBracketIndex <= openBracketIndex + 1 ||
            closeBracketIndex + 1 >= text.Length ||
            text[closeBracketIndex + 1] != '(')
        {
            return false;
        }

        var closeParenIndex = text.IndexOf(')', closeBracketIndex + 2);
        if (closeParenIndex < 0)
        {
            return false;
        }

        var displayText = text.Substring(openBracketIndex + 1, closeBracketIndex - openBracketIndex - 1);
        var rawTarget = text.Substring(closeBracketIndex + 2, closeParenIndex - closeBracketIndex - 2).Trim();
        if (string.IsNullOrWhiteSpace(displayText) ||
            !TryParseMarkdownLinkTarget(rawTarget, out var lookupText, out var lineRange))
        {
            return false;
        }

        consumedLength = closeParenIndex - openBracketIndex + 1;
        token = new ProjectFilePromptToken(
            ProjectFilePromptTokenKind.Reference,
            openBracketIndex,
            consumedLength,
            text.Substring(openBracketIndex, consumedLength),
            lookupText,
            lineRange,
            false,
            displayText);
        return true;
    }

    private static bool TryParseActiveReference(
        string text,
        int atIndex,
        int caretIndex,
        out ProjectFilePromptActiveReference activeReference)
    {
        activeReference = default!;

        var index = atIndex + 1;
        if (index < text.Length && text[index] == '@')
        {
            return false;
        }

        if (caretIndex < index)
        {
            return false;
        }

        if (caretIndex == index)
        {
            activeReference = new ProjectFilePromptActiveReference(atIndex, 1, string.Empty);
            return true;
        }

        if (index < text.Length && text[index] == '"')
        {
            var pathStart = index + 1;
            var closingQuoteIndex = text.IndexOf('"', pathStart);
            var replaceEnd = closingQuoteIndex >= 0 ? closingQuoteIndex + 1 : text.Length;
            if (caretIndex < pathStart || caretIndex > replaceEnd)
            {
                return false;
            }

            var queryLength = Math.Max(0, replaceEnd - pathStart);
            var queryText = queryLength == 0 ? string.Empty : text.Substring(pathStart, queryLength);
            activeReference = new ProjectFilePromptActiveReference(
                atIndex,
                replaceEnd - atIndex,
                queryText);
            return true;
        }

        var tokenEnd = index;
        while (tokenEnd < text.Length &&
               !ReferenceTerminators.Contains(text[tokenEnd]) &&
               text[tokenEnd] != ':')
        {
            tokenEnd++;
        }

        if (tokenEnd == index)
        {
            return false;
        }

        if (caretIndex < index || caretIndex > tokenEnd)
        {
            return false;
        }

        activeReference = new ProjectFilePromptActiveReference(
            atIndex,
            tokenEnd - atIndex,
            text.Substring(index, tokenEnd - index));
        return true;
    }

    private static ProjectFileLineRange? ParseRange(string text, ref int index)
    {
        if (index >= text.Length || text[index] != ':')
        {
            return null;
        }

        var rangeStart = index;
        index++;
        if (!TryReadPositiveInteger(text, ref index, out var startLine))
        {
            index = rangeStart;
            return null;
        }

        var endLine = startLine;
        if (index < text.Length && text[index] == '-')
        {
            index++;
            if (!TryReadPositiveInteger(text, ref index, out endLine))
            {
                index = rangeStart;
                return null;
            }
        }

        return new ProjectFileLineRange(startLine, endLine);
    }

    private static bool TryParseMarkdownLinkTarget(
        string target,
        out string lookupText,
        out ProjectFileLineRange? lineRange)
    {
        lookupText = string.Empty;
        lineRange = null;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var trimmed = target.Trim();
        var pathText = trimmed;
        var rangeSeparatorIndex = trimmed.LastIndexOf(':');
        if (rangeSeparatorIndex > 0 &&
            TryParseMarkdownLinkRange(trimmed, rangeSeparatorIndex + 1, out lineRange))
        {
            pathText = trimmed[..rangeSeparatorIndex];
        }

        if (!IsProjectRelativeMarkdownLinkTarget(pathText))
        {
            return false;
        }

        lookupText = pathText;
        return true;
    }

    private static bool TryParseMarkdownLinkRange(
        string text,
        int startIndex,
        out ProjectFileLineRange? lineRange)
    {
        lineRange = null;
        var index = startIndex;
        if (!TryReadPositiveInteger(text, ref index, out var startLine))
        {
            return false;
        }

        var endLine = startLine;
        if (index < text.Length && text[index] == '-')
        {
            index++;
            if (!TryReadPositiveInteger(text, ref index, out endLine))
            {
                return false;
            }
        }

        if (index != text.Length)
        {
            return false;
        }

        lineRange = new ProjectFileLineRange(startLine, endLine);
        return true;
    }

    private static bool IsProjectRelativeMarkdownLinkTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var trimmed = target.Trim().Replace('\\', '/');
        if (trimmed.StartsWith('/') ||
            trimmed.StartsWith('#') ||
            trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length >= 2 &&
            char.IsAsciiLetter(trimmed[0]) &&
            trimmed[1] == ':')
        {
            return false;
        }

        return !trimmed.Contains(':');
    }

    private static bool TryReadPositiveInteger(string text, ref int index, out int value)
    {
        var start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        if (index == start ||
            !int.TryParse(text.AsSpan(start, index - start), out value) ||
            value <= 0)
        {
            value = 0;
            return false;
        }

        return true;
    }

    private static bool IsReferenceBoundary(string text, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var previous = text[index - 1];
        return char.IsWhiteSpace(previous) ||
               previous is '(' or '[' or '{' or '"' or '\'' or '<' or '\n' or '\r';
    }
}
