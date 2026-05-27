using CodeAlta.Agent;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;

namespace CodeAlta.App;

internal sealed class PromptImageCapabilityContext
{
    private readonly Func<SessionViewDescriptor?> _getSelectedThread;
    private readonly Func<string, OpenThreadState?> _findOpenThread;
    private readonly Func<ModelProviderId> _getPreferredProviderId;
    private readonly IReadOnlyDictionary<string, ModelProviderState> _modelProviderStates;

    public PromptImageCapabilityContext(
        Func<SessionViewDescriptor?> getSelectedThread,
        Func<string, OpenThreadState?> findOpenThread,
        Func<ModelProviderId> getPreferredProviderId,
        IReadOnlyDictionary<string, ModelProviderState> modelProviderStates)
    {
        ArgumentNullException.ThrowIfNull(getSelectedThread);
        ArgumentNullException.ThrowIfNull(findOpenThread);
        ArgumentNullException.ThrowIfNull(getPreferredProviderId);
        ArgumentNullException.ThrowIfNull(modelProviderStates);

        _getSelectedThread = getSelectedThread;
        _findOpenThread = findOpenThread;
        _getPreferredProviderId = getPreferredProviderId;
        _modelProviderStates = modelProviderStates;
    }

    public bool CurrentPromptModelSupportsImageInput()
    {
        var (providerId, model) = ResolveCurrentPromptModel();
        return AgentModelCapabilityHelper.SupportsImageInput(providerId, model);
    }

    public string BuildCurrentPromptImageUnsupportedMessage()
    {
        var (providerId, model) = ResolveCurrentPromptModel();
        var modelName = model?.DisplayName ?? model?.Id ?? "the selected model";
        return $"Image paste is not available because {modelName} on {providerId.Value} does not advertise image input support.";
    }

    private (ModelProviderId ProviderId, AgentModelInfo? Model) ResolveCurrentPromptModel()
    {
        var selectedThread = _getSelectedThread();
        var selectedTab = selectedThread is null ? null : _findOpenThread(selectedThread.ThreadId);
        var providerId = selectedTab?.ProviderId ?? (selectedThread is { } thread
            ? new ModelProviderId(thread.ResolvedProviderKey)
            : _getPreferredProviderId());
        if (!_modelProviderStates.TryGetValue(providerId.Value, out var backendState))
        {
            return (providerId, null);
        }

        var modelId = selectedTab?.ModelId ?? backendState.SelectedModelId;
        var selectedModel = !string.IsNullOrWhiteSpace(modelId)
            ? backendState.Models.FirstOrDefault(candidate => string.Equals(candidate.Id, modelId, StringComparison.Ordinal))
            : null;
        selectedModel ??= ModelProviderPresentation.GetSelectedModel(backendState) ??
            (backendState.Models.Count == 1 ? backendState.Models[0] : null);
        return (providerId, selectedModel);
    }
}
