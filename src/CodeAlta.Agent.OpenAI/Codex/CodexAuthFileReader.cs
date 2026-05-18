using System.Text.Json;

namespace CodeAlta.Agent.OpenAI.Codex;

internal static class CodexAuthFileReader
{
    public static string ResolveCodexHome(
        IReadOnlyDictionary<string, string?>? environment = null,
        string? userProfile = null,
        string? home = null)
    {
        environment ??= Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(
                static entry => (string)entry.Key,
                static entry => entry.Value?.ToString(),
                StringComparer.OrdinalIgnoreCase);

        if (environment.TryGetValue("CODEX_HOME", out var codexHome) &&
            !string.IsNullOrWhiteSpace(codexHome))
        {
            return codexHome.Trim();
        }

        if (OperatingSystem.IsWindows())
        {
            userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, ".codex");
        }

        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex");
    }

    public static async ValueTask<OpenAICodexSubscriptionCredential?> ReadAuthJsonAsync(
        string codexHome,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codexHome);
        var path = Path.Combine(codexHome, "auth.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var accessToken = GetString(tokens, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        return new OpenAICodexSubscriptionCredential
        {
            Issuer = OpenAICodexSubscriptionOAuthDefaults.Issuer,
            ClientId = OpenAICodexSubscriptionOAuthDefaults.ClientId,
            AccessToken = accessToken,
            RefreshToken = GetString(tokens, "refresh_token"),
            IdToken = GetString(tokens, "id_token"),
            ExpiresAt = GetExpiresAt(tokens),
            AccountId = GetString(tokens, "account_id"),
            AccountLabel = GetString(tokens, "account_label"),
            IsFedRamp = GetBoolean(tokens, "is_fedramp") ?? false,
            Scopes = GetScopes(tokens),
        };
    }

    public static async ValueTask<OpenAICodexSubscriptionCredential?> ImportAuthJsonAsync(
        string codexHome,
        IOpenAICodexSubscriptionCredentialStore credentialStore,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        var credential = await ReadAuthJsonAsync(codexHome, cancellationToken).ConfigureAwait(false);
        if (credential is not null)
        {
            await credentialStore.SaveAsync(providerKey, credential, cancellationToken).ConfigureAwait(false);
        }

        return credential;
    }

    private static DateTimeOffset GetExpiresAt(JsonElement tokens)
    {
        var text = GetString(tokens, "expires_at");
        if (DateTimeOffset.TryParse(text, out var parsed))
        {
            return parsed;
        }

        if (tokens.TryGetProperty("expires_at", out var expiresAt) &&
            expiresAt.ValueKind == JsonValueKind.Number &&
            expiresAt.TryGetInt64(out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return DateTimeOffset.MinValue;
    }

    private static List<string> GetScopes(JsonElement tokens)
    {
        if (tokens.TryGetProperty("scopes", out var scopes) &&
            scopes.ValueKind == JsonValueKind.Array)
        {
            return scopes.EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        var scopeText = GetString(tokens, "scope");
        return string.IsNullOrWhiteSpace(scopeText)
            ? []
            : scopeText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? GetBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
}
