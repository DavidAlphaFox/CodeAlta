namespace CodeAlta.Orchestration.Runtime.Prompts;

/// <summary>
/// Provides reusable prompt text normalization helpers for headless and frontend session-view flows.
/// </summary>
public static class SessionPromptText
{
    /// <summary>The default maximum length used for generated initial session titles.</summary>
    public const int DefaultMaxInitialSessionTitleLength = 80;

    /// <summary>
    /// Normalizes prompt text into single-line display text.
    /// </summary>
    /// <param name="prompt">The prompt text.</param>
    /// <returns>The normalized prompt text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="prompt"/> is <see langword="null"/>.</exception>
    public static string NormalizeForDisplay(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var normalized = prompt
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim();

        return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Creates an initial session title from prompt text.
    /// </summary>
    /// <param name="prompt">The prompt text.</param>
    /// <param name="maxLength">The maximum title length.</param>
    /// <returns>A normalized initial session title.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="prompt"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxLength"/> is less than four.</exception>
    public static string CreateInitialSessionTitle(string prompt, int maxLength = DefaultMaxInitialSessionTitleLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (maxLength < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "The maximum title length must be at least four characters.");
        }

        var normalized = NormalizeForDisplay(prompt);
        if (normalized.Length == 0)
        {
            return prompt.Trim();
        }

        var sentenceLength = FindFirstSentenceLength(normalized);
        var candidate = sentenceLength > 0
            ? normalized[..sentenceLength]
            : normalized;

        if (candidate.Length <= maxLength)
        {
            return candidate;
        }

        return candidate[..(maxLength - 3)].TrimEnd() + "...";
    }

    private static int FindFirstSentenceLength(string content)
    {
        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (ch is not ('.' or '!' or '?'))
            {
                continue;
            }

            if (i == content.Length - 1 || char.IsWhiteSpace(content[i + 1]))
            {
                return i + 1;
            }
        }

        return 0;
    }
}
