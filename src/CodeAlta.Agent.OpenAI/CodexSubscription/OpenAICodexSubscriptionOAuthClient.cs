using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeAlta.Agent.OpenAI.CodexSubscription;

internal sealed class OpenAICodexSubscriptionOAuthClient
{
    private readonly HttpClient _httpClient;

    public OpenAICodexSubscriptionOAuthClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public static OpenAICodexSubscriptionPkce CreatePkce()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var verifier = Base64UrlEncode(bytes);
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new OpenAICodexSubscriptionPkce(verifier, challenge);
    }

    public static string CreateState()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    public static Uri BuildAuthorizeUri(
        OpenAICodexSubscriptionPkce pkce,
        string state,
        string? allowedWorkspaceId = null)
    {
        ArgumentNullException.ThrowIfNull(pkce);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["response_type"] = "code",
            ["client_id"] = OpenAICodexSubscriptionOAuthDefaults.ClientId,
            ["redirect_uri"] = OpenAICodexSubscriptionOAuthDefaults.RedirectUri,
            ["scope"] = OpenAICodexSubscriptionOAuthDefaults.Scope,
            ["code_challenge"] = pkce.Challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true",
            ["originator"] = "codealta",
        };
        if (!string.IsNullOrWhiteSpace(allowedWorkspaceId))
        {
            query["allowed_workspace_id"] = allowedWorkspaceId.Trim();
        }

        return new Uri(OpenAICodexSubscriptionOAuthDefaults.AuthorizeEndpoint + "?" + BuildFormUrlEncoded(query));
    }

    public static void ValidateState(string expected, string actual)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(actual)))
        {
            throw new InvalidOperationException("OAuth state mismatch.");
        }
    }

    public async Task<OpenAICodexSubscriptionCredential> ExchangeAuthorizationCodeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = OpenAICodexSubscriptionOAuthDefaults.ClientId,
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri,
            });
        using var response = await _httpClient.PostAsync(
                OpenAICodexSubscriptionOAuthDefaults.TokenEndpoint,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        return await ReadTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpenAICodexSubscriptionCredential> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = OpenAICodexSubscriptionOAuthDefaults.ClientId,
                ["refresh_token"] = refreshToken,
            });
        using var response = await _httpClient.PostAsync(
                OpenAICodexSubscriptionOAuthDefaults.TokenEndpoint,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        return await ReadTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpenAICodexSubscriptionDeviceCode> RequestDeviceCodeAsync(
        CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client_id"] = OpenAICodexSubscriptionOAuthDefaults.ClientId,
                ["scope"] = OpenAICodexSubscriptionOAuthDefaults.Scope,
            });
        using var response = await _httpClient.PostAsync(
                OpenAICodexSubscriptionOAuthDefaults.DeviceUserCodeEndpoint,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        return new OpenAICodexSubscriptionDeviceCode(
            RequiredString(root, "device_code"),
            RequiredString(root, "user_code"),
            GetString(root, "verification_uri") ?? OpenAICodexSubscriptionOAuthDefaults.DeviceVerificationUri,
            TimeSpan.FromSeconds(GetInt32(root, "expires_in") ?? 900),
            TimeSpan.FromSeconds(GetInt32(root, "interval") ?? 5));
    }

    public async Task<OpenAICodexSubscriptionCredential> PollDeviceTokenAsync(
        OpenAICodexSubscriptionDeviceCode deviceCode,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceCode);
        timeProvider ??= TimeProvider.System;
        var expiresAt = timeProvider.GetUtcNow() + deviceCode.ExpiresIn;
        while (timeProvider.GetUtcNow() < expiresAt)
        {
            using var content = new FormUrlEncodedContent(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["client_id"] = OpenAICodexSubscriptionOAuthDefaults.ClientId,
                    ["device_code"] = deviceCode.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["redirect_uri"] = OpenAICodexSubscriptionOAuthDefaults.DeviceRedirectUri,
                });
            using var response = await _httpClient.PostAsync(
                    OpenAICodexSubscriptionOAuthDefaults.DeviceTokenEndpoint,
                    content,
                    cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return await ReadTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var error = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(error, "authorization_pending", StringComparison.Ordinal) &&
                !string.Equals(error, "slow_down", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Device authorization failed: {error ?? response.StatusCode.ToString()}.");
            }

            var delay = string.Equals(error, "slow_down", StringComparison.Ordinal)
                ? deviceCode.Interval + TimeSpan.FromSeconds(5)
                : deviceCode.Interval;
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Device authorization expired before login completed.");
    }

    public static OpenAICodexSubscriptionCredential CredentialFromTokenJson(JsonElement root, DateTimeOffset now)
    {
        var expiresIn = GetInt32(root, "expires_in");
        return new OpenAICodexSubscriptionCredential
        {
            Issuer = OpenAICodexSubscriptionOAuthDefaults.Issuer,
            ClientId = OpenAICodexSubscriptionOAuthDefaults.ClientId,
            AccessToken = RequiredString(root, "access_token"),
            RefreshToken = GetString(root, "refresh_token"),
            IdToken = GetString(root, "id_token"),
            ExpiresAt = expiresIn is null ? now : now.AddSeconds(expiresIn.Value),
            Scopes = (GetString(root, "scope") ?? OpenAICodexSubscriptionOAuthDefaults.Scope)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
        };
    }

    private async Task<OpenAICodexSubscriptionCredential> ReadTokenResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return CredentialFromTokenJson(document.RootElement, DateTimeOffset.UtcNow);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (stream.Length == 0)
        {
            return null;
        }

        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return GetString(document.RootElement, "error");
    }

    private static string BuildFormUrlEncoded(IReadOnlyDictionary<string, string?> values)
        => string.Join(
            "&",
            values
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.Value))
                .Select(static entry => WebUtility.UrlEncode(entry.Key) + "=" + WebUtility.UrlEncode(entry.Value)));

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string RequiredString(JsonElement element, string propertyName)
        => GetString(element, propertyName)
            ?? throw new InvalidOperationException($"OAuth response did not include '{propertyName}'.");

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? GetInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
}

internal sealed record OpenAICodexSubscriptionPkce(string Verifier, string Challenge);

internal sealed record OpenAICodexSubscriptionDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    TimeSpan ExpiresIn,
    TimeSpan Interval);
