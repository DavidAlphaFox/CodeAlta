namespace CodeAlta.Agent.OpenAI.Codex;

internal static class OpenAICodexSubscriptionOAuthDefaults
{
    public const string Issuer = "https://auth.openai.com";
    public const string AuthorizeEndpoint = "https://auth.openai.com/oauth/authorize";
    public const string TokenEndpoint = "https://auth.openai.com/oauth/token";
    public const int LocalPort = 1455;
    public const string RedirectUri = "http://localhost:1455/auth/callback";
    public const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    public const string Scope = "openid profile email offline_access api.connectors.read api.connectors.invoke";
    public const string DeviceUserCodeEndpoint = "https://auth.openai.com/api/accounts/deviceauth/usercode";
    public const string DeviceTokenEndpoint = "https://auth.openai.com/api/accounts/deviceauth/token";
    public const string DeviceVerificationUri = "https://auth.openai.com/codex/device";
    public const string DeviceRedirectUri = "https://auth.openai.com/deviceauth/callback";
}
