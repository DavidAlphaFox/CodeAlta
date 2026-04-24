using System.Net;
namespace CodeAlta.Agent.OpenAI.CodexSubscription;

internal sealed class OpenAICodexSubscriptionLoginManager
{
    private readonly IOpenAICodexSubscriptionCredentialStore _credentialStore;
    private readonly OpenAICodexSubscriptionOAuthClient _oauthClient;
    private readonly string _providerKey;

    public OpenAICodexSubscriptionLoginManager(
        IOpenAICodexSubscriptionCredentialStore credentialStore,
        OpenAICodexSubscriptionOAuthClient oauthClient,
        string providerKey)
    {
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(oauthClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        _credentialStore = credentialStore;
        _oauthClient = oauthClient;
        _providerKey = providerKey;
    }

    public OpenAICodexSubscriptionBrowserLogin BeginBrowserLogin(string? allowedWorkspaceId = null)
    {
        var pkce = OpenAICodexSubscriptionOAuthClient.CreatePkce();
        var state = OpenAICodexSubscriptionOAuthClient.CreateState();
        return new OpenAICodexSubscriptionBrowserLogin(
            OpenAICodexSubscriptionOAuthClient.BuildAuthorizeUri(pkce, state, allowedWorkspaceId),
            pkce,
            state);
    }

    public async ValueTask<OpenAICodexSubscriptionCredential> CompleteBrowserLoginAsync(
        OpenAICodexSubscriptionBrowserLogin login,
        Uri callbackUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(callbackUri);

        var query = ParseQuery(callbackUri.Query);
        if (query.TryGetValue("error", out var error) && !string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException("OAuth login failed: " + error);
        }

        if (!query.TryGetValue("state", out var actualState))
        {
            throw new InvalidOperationException("OAuth callback did not include state.");
        }

        OpenAICodexSubscriptionOAuthClient.ValidateState(login.State, actualState);
        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("OAuth callback did not include an authorization code.");
        }

        var credential = await _oauthClient.ExchangeAuthorizationCodeAsync(
            code,
            login.Pkce.Verifier,
            OpenAICodexSubscriptionOAuthDefaults.RedirectUri,
            cancellationToken).ConfigureAwait(false);
        await SaveCredentialAsync(credential, cancellationToken).ConfigureAwait(false);
        return credential;
    }

    public async ValueTask<OpenAICodexSubscriptionCredential> CompleteDeviceLoginAsync(
        Func<OpenAICodexSubscriptionDeviceCode, CancellationToken, ValueTask> showDeviceCodeAsync,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(showDeviceCodeAsync);

        var deviceCode = await _oauthClient.RequestDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        await showDeviceCodeAsync(deviceCode, cancellationToken).ConfigureAwait(false);
        var credential = await _oauthClient.PollDeviceTokenAsync(
            deviceCode,
            timeProvider,
            cancellationToken).ConfigureAwait(false);
        await SaveCredentialAsync(credential, cancellationToken).ConfigureAwait(false);
        return credential;
    }

    public async ValueTask<OpenAICodexSubscriptionCredential> WaitForBrowserCallbackAsync(
        OpenAICodexSubscriptionBrowserLogin login,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);

        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:1455/auth/callback/");
        listener.Start();
        using var registration = cancellationToken.Register(static state => ((HttpListener)state!).Stop(), listener);
        try
        {
            var context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var credential = await CompleteBrowserLoginAsync(
                    login,
                    context.Request.Url ?? throw new InvalidOperationException("OAuth callback URL was unavailable."),
                    cancellationToken).ConfigureAwait(false);
                await WriteBrowserCallbackResponseAsync(
                    context.Response,
                    "CodeAlta login complete. You may close this browser tab.",
                    cancellationToken).ConfigureAwait(false);
                return credential;
            }
            catch
            {
                await WriteBrowserCallbackResponseAsync(
                    context.Response,
                    "CodeAlta login failed. Return to CodeAlta and try again.",
                    cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            listener.Stop();
        }
    }

    public async ValueTask DeleteCredentialAsync(CancellationToken cancellationToken = default)
        => await _credentialStore.DeleteAsync(_providerKey, cancellationToken).ConfigureAwait(false);

    private async ValueTask SaveCredentialAsync(
        OpenAICodexSubscriptionCredential credential,
        CancellationToken cancellationToken)
    {
        PopulateLocalMetadata(credential);
        await _credentialStore.SaveAsync(_providerKey, credential, cancellationToken).ConfigureAwait(false);
    }

    private static void PopulateLocalMetadata(OpenAICodexSubscriptionCredential credential)
    {
        credential.AccountId ??= OpenAICodexSubscriptionAuthManager.TryExtractAccountIdFromJwt(credential.AccessToken)
            ?? OpenAICodexSubscriptionAuthManager.TryExtractAccountIdFromJwt(credential.IdToken);
    }

    private static async ValueTask WriteBrowserCallbackResponseAsync(
        HttpListenerResponse response,
        string message,
        CancellationToken cancellationToken)
    {
        response.StatusCode = 200;
        response.ContentType = "text/plain; charset=utf-8";
        await response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(message), cancellationToken).ConfigureAwait(false);
        response.Close();
    }

    private static Dictionary<string, string> ParseQuery(string query)
        => query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(static pair => pair.Split('=', 2))
            .Where(static parts => parts.Length == 2)
            .ToDictionary(
                static parts => Uri.UnescapeDataString(parts[0]),
                static parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')),
                StringComparer.Ordinal);
}

internal sealed record OpenAICodexSubscriptionBrowserLogin(
    Uri AuthorizeUri,
    OpenAICodexSubscriptionPkce Pkce,
    string State);
