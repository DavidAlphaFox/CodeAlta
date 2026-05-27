using CodeAlta.Agent;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;

namespace CodeAlta.App;

internal sealed class SessionProviderSwitchCoordinator
{
    private readonly IReadOnlyDictionary<string, ModelProviderState> _modelProviderStates;
    private readonly IReadOnlyDictionary<string, LocalRuntimeBackendInfo> _localRuntimeBackends;
    private readonly Func<OpenSessionState, Task> _applySessionPreferenceAsync;
    private readonly Func<string, Task<bool>> _detachRuntimeSessionAsync;
    private readonly Action<SessionViewDescriptor> _updateSessionState;
    private readonly Func<Task> _persistViewStateAsync;

    public SessionProviderSwitchCoordinator(
        CodeAltaConfigStore configStore,
        IReadOnlyDictionary<string, ModelProviderState> modelProviderStates,
        Func<OpenSessionState, Task> applySessionPreferenceAsync,
        Func<string, Task<bool>> detachRuntimeSessionAsync,
        Action<SessionViewDescriptor> updateSessionState,
        Func<Task> persistViewStateAsync)
    {
        ArgumentNullException.ThrowIfNull(configStore);
        ArgumentNullException.ThrowIfNull(modelProviderStates);
        ArgumentNullException.ThrowIfNull(applySessionPreferenceAsync);
        ArgumentNullException.ThrowIfNull(detachRuntimeSessionAsync);
        ArgumentNullException.ThrowIfNull(updateSessionState);
        ArgumentNullException.ThrowIfNull(persistViewStateAsync);

        _modelProviderStates = modelProviderStates;
        _applySessionPreferenceAsync = applySessionPreferenceAsync;
        _detachRuntimeSessionAsync = detachRuntimeSessionAsync;
        _updateSessionState = updateSessionState;
        _persistViewStateAsync = persistViewStateAsync;
        _localRuntimeBackends = configStore.LoadGlobalProviderDefinitions(includeDisabled: true)
            .Where(static definition => TryMapProtocolFamily(definition.ProviderType, out _))
            .Select(definition =>
            {
                TryMapProtocolFamily(definition.ProviderType, out var protocolFamily);
                return new KeyValuePair<string, LocalRuntimeBackendInfo>(
                    definition.ProviderKey,
                    new LocalRuntimeBackendInfo(
                        new ModelProviderId(definition.ProviderKey),
                        definition.ProviderKey,
                        protocolFamily!));
            })
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public bool CanSelectSessionProvider(SessionViewDescriptor session, OpenSessionState tab)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tab);

        return !tab.StatusBusy &&
               tab.ActiveRunId is null &&
               IsSwitchableSourceProvider(new ModelProviderId(session.ProviderId));
    }

    public bool CanSwitchSessionProvider(
        SessionViewDescriptor session,
        OpenSessionState tab,
        ModelProviderId targetProviderId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tab);

        return CanSelectSessionProvider(session, tab) &&
               !string.Equals(session.ProviderId, targetProviderId.Value, StringComparison.OrdinalIgnoreCase) &&
               TryGetLocalRuntimeBackendInfo(targetProviderId, out _) &&
               _modelProviderStates.TryGetValue(targetProviderId.Value, out var targetState) &&
               targetState.Availability == ModelProviderAvailability.Ready;
    }

    public async Task<bool> SwitchSessionProviderAsync(
        SessionViewDescriptor session,
        OpenSessionState tab,
        ModelProviderId targetProviderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tab);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanSwitchSessionProvider(session, tab, targetProviderId) ||
            !TryGetLocalRuntimeBackendInfo(targetProviderId, out var targetBackend))
        {
            return false;
        }

        var timestamp = DateTimeOffset.UtcNow;
        var oldSessionId = session.SessionId;
        var oldSessionProviderId = session.ProviderId;
        var oldSessionProviderKey = session.ProviderKey;
        var oldSessionUpdatedAt = session.UpdatedAt;
        var oldSessionModelId = session.ModelId;
        var oldSessionReasoningEffort = session.ReasoningEffort;
        var oldTabProviderId = tab.ProviderId;
        var oldTabModelId = tab.ModelId;
        var oldTabReasoningEffort = tab.ReasoningEffort;
        var oldTabUsage = tab.Usage;

        session.ProviderId = targetBackend.ProviderId.Value;
        session.ProviderKey = targetBackend.ProviderKey;
        session.UpdatedAt = timestamp;

        tab.ProviderId = targetBackend.ProviderId;
        tab.ModelId = null;
        tab.ReasoningEffort = null;
        tab.Usage = null;

        try
        {
            await _applySessionPreferenceAsync(tab);
            NormalizeTargetModelSelection(tab, targetBackend.ProviderId);
            session.ModelId = tab.ModelId;
            session.ReasoningEffort = tab.ReasoningEffort;
            if (!string.IsNullOrWhiteSpace(oldSessionId))
            {
                await _detachRuntimeSessionAsync(oldSessionId);
            }
        }
        catch
        {
            session.ProviderId = oldSessionProviderId;
            session.ProviderKey = oldSessionProviderKey;
            session.UpdatedAt = oldSessionUpdatedAt;
            session.ModelId = oldSessionModelId;
            session.ReasoningEffort = oldSessionReasoningEffort;
            tab.ProviderId = oldTabProviderId;
            tab.ModelId = oldTabModelId;
            tab.ReasoningEffort = oldTabReasoningEffort;
            tab.Usage = oldTabUsage;
            throw;
        }

        _updateSessionState(session);
        await _persistViewStateAsync();
        return true;
    }

    private bool TryGetLocalRuntimeBackendInfo(ModelProviderId providerId, out LocalRuntimeBackendInfo backendInfo)
    {
        return _localRuntimeBackends.TryGetValue(providerId.Value, out backendInfo!);
    }

    private bool IsSwitchableSourceProvider(ModelProviderId providerId)
        => TryGetLocalRuntimeBackendInfo(providerId, out _) || IsNativeProvider(providerId);

    private void NormalizeTargetModelSelection(OpenSessionState tab, ModelProviderId targetProviderId)
    {
        if (!_modelProviderStates.TryGetValue(targetProviderId.Value, out var targetState) ||
            targetState.Models.Count == 0 ||
            string.IsNullOrWhiteSpace(tab.ModelId) ||
            targetState.Models.Any(model => string.Equals(model.Id, tab.ModelId, StringComparison.Ordinal)))
        {
            return;
        }

        tab.ModelId = ModelProviderPresentation.ResolvePreferredModelId(targetState.Models, targetState.SelectedModelId);
        var selectedModel = ModelProviderPreferenceCoordinator.FindModel(targetState.Models, tab.ModelId);
        tab.ReasoningEffort = ModelProviderPresentation.ResolvePreferredReasoningEffort(
            selectedModel,
            targetState.SelectedReasoningEffort);
    }

    private static bool IsNativeProvider(ModelProviderId providerId)
        => string.Equals(providerId.Value, ModelProviderIds.Codex.Value, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(providerId.Value, ModelProviderIds.Copilot.Value, StringComparison.OrdinalIgnoreCase);

    private static bool TryMapProtocolFamily(string? providerType, out string? protocolFamily)
    {
        protocolFamily = providerType?.Trim().ToLowerInvariant() switch
        {
            "openai-chat" => "openai-chat",
            "openai-responses" => "openai-responses",
            "azure-openai" => "openai-chat",
            "codex" => "codex",
            "copilot" => "copilot",
            "xai" => "xai",
            "anthropic" => "anthropic-messages",
            "google-genai" => "google-genai",
            "vertex-ai" => "google-genai",
            _ => null,
        };

        return protocolFamily is not null;
    }

    private sealed record LocalRuntimeBackendInfo(
        ModelProviderId ProviderId,
        string ProviderKey,
        string ProtocolFamily);
}
