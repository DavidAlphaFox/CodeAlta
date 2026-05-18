using System.Text.RegularExpressions;

namespace CodeAlta.Agent.OpenAI.Codex;

internal static class OpenAICodexSubscriptionSecretRedactor
{
    public const string Redacted = "[REDACTED]";

    private static readonly Regex JwtPattern = new(
        @"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}(?![A-Za-z0-9_-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LabeledSecretPattern = new(
        @"(?i)\b(?<name>code|authorization_code|code_verifier|pkce_verifier|pkce verifier)(?<sep>\s*[:=]\s*)(?<value>[^\s&]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string? text, OpenAICodexSubscriptionCredential? credential = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var redacted = text;
        if (credential is not null)
        {
            redacted = RedactValue(redacted, credential.AccessToken);
            redacted = RedactValue(redacted, credential.RefreshToken);
            redacted = RedactValue(redacted, credential.IdToken);
        }

        redacted = RedactBearerToken(redacted);
        redacted = LabeledSecretPattern.Replace(
            redacted,
            static match => match.Groups["name"].Value + match.Groups["sep"].Value + Redacted);
        redacted = JwtPattern.Replace(redacted, Redacted);
        return redacted;
    }

    public static string RedactValue(string text, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return text;
        }

        return text.Replace(secret, Redacted, StringComparison.Ordinal);
    }

    private static string RedactBearerToken(string text)
    {
        const string marker = "Bearer ";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return text;
        }

        var tokenStart = start + marker.Length;
        var tokenEnd = tokenStart;
        while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd]))
        {
            tokenEnd++;
        }

        return text[..tokenStart] + Redacted + text[tokenEnd..];
    }
}
