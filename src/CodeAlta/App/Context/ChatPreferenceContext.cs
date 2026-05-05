using CodeAlta.App.State;
using CodeAlta.Agent;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;

namespace CodeAlta.App.Context;

internal sealed class ChatPreferenceContext
{
    private readonly Action<ChatBackendState> _applyDraftBackendPreference;
    private readonly Action<OpenThreadState> _applyThreadPreference;
    private readonly Action<AgentBackendId, string?, AgentReasoningEffort?> _rememberGlobalBackendPreference;
    private readonly Action<string, string?, AgentReasoningEffort?, bool> _rememberThreadPreference;

    public ChatPreferenceContext(
        Action<ChatBackendState> applyDraftBackendPreference,
        Action<OpenThreadState> applyThreadPreference,
        Action<AgentBackendId, string?, AgentReasoningEffort?> rememberGlobalBackendPreference,
        Action<string, string?, AgentReasoningEffort?, bool> rememberThreadPreference)
    {
        ArgumentNullException.ThrowIfNull(applyDraftBackendPreference);
        ArgumentNullException.ThrowIfNull(applyThreadPreference);
        ArgumentNullException.ThrowIfNull(rememberGlobalBackendPreference);
        ArgumentNullException.ThrowIfNull(rememberThreadPreference);

        _applyDraftBackendPreference = applyDraftBackendPreference;
        _applyThreadPreference = applyThreadPreference;
        _rememberGlobalBackendPreference = rememberGlobalBackendPreference;
        _rememberThreadPreference = rememberThreadPreference;
    }

    public void ApplyDraftBackendPreference(ChatBackendState backendState)
    {
        ArgumentNullException.ThrowIfNull(backendState);
        _applyDraftBackendPreference(backendState);
    }

    public void ApplyThreadPreference(OpenThreadState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _applyThreadPreference(tab);
    }

    public void RememberGlobalBackendPreference(
        AgentBackendId backendId,
        string? modelId,
        AgentReasoningEffort? reasoningEffort)
        => _rememberGlobalBackendPreference(backendId, modelId, reasoningEffort);

    public void RememberThreadPreference(
        string threadId,
        string? modelId,
        AgentReasoningEffort? reasoningEffort,
        bool persistNow)
        => _rememberThreadPreference(threadId, modelId, reasoningEffort, persistNow);
}
