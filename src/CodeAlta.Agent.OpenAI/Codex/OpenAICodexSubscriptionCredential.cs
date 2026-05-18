using System.Text.Json.Serialization;

namespace CodeAlta.Agent.OpenAI.Codex;

internal sealed class OpenAICodexSubscriptionCredential
{
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = OpenAICodexSubscriptionOAuthDefaults.Issuer;

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = OpenAICodexSubscriptionOAuthDefaults.ClientId;

    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    [JsonPropertyName("account_label")]
    public string? AccountLabel { get; set; }

    [JsonPropertyName("is_fedramp")]
    public bool IsFedRamp { get; set; }

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = [];
}
