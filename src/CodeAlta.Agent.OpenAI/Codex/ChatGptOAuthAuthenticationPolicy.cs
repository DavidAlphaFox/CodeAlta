using System.ClientModel.Primitives;

namespace CodeAlta.Agent.OpenAI.Codex;

internal sealed class ChatGptOAuthAuthenticationPolicy : AuthenticationPolicy
{
    private readonly OpenAICodexSubscriptionAuthManager _authManager;

    public ChatGptOAuthAuthenticationPolicy(OpenAICodexSubscriptionAuthManager authManager)
    {
        ArgumentNullException.ThrowIfNull(authManager);
        _authManager = authManager;
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ApplyAsync(message).ConfigureAwait(false);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private void Apply(PipelineMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var token = _authManager
            .GetAccessTokenAsync(message.CancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        message.Request.Headers.Set("Authorization", "Bearer " + token);
    }

    private async ValueTask ApplyAsync(PipelineMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var token = await _authManager.GetAccessTokenAsync(message.CancellationToken).ConfigureAwait(false);
        message.Request.Headers.Set("Authorization", "Bearer " + token);
    }
}
