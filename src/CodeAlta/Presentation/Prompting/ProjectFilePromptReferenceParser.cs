using System.Buffers;
using CodeAlta.Search;

namespace CodeAlta.Presentation.Prompting;

internal static class ProjectFilePromptReferenceParser
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
