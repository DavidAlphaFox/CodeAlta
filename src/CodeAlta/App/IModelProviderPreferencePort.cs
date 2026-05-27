using CodeAlta.App.State;
using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;

namespace CodeAlta.App;

internal sealed record ModelProviderPreference(
    ModelProviderId ModelProviderId,
    string? ModelId = null,
    AgentReasoningEffort? ReasoningEffort = null)
{
    public ModelProviderPreference Normalize()
        => this with { ModelId = string.IsNullOrWhiteSpace(ModelId) ? null : ModelId };
}

internal interface IModelProviderPreferencePort
{
    ModelProviderId GetPreferredModelProviderId(ProjectId projectId);

    bool IsModelProviderReady(ModelProviderId modelProviderId);

    void ApplyDraftPreference(PromptSessionBinding promptSession, ModelProviderPreference preference);

    void ApplyThreadPreference(string threadId, ModelProviderPreference preference);

    void RememberProjectPreference(ProjectId projectId, ModelProviderPreference preference);

    void RememberThreadPreference(string threadId, ModelProviderPreference preference, bool persistNow);

    void ApplyDraftModelProviderState(ModelProviderState modelProviderState);

    void ApplyThreadModelProviderState(OpenThreadState threadState);

    void RememberGlobalPreference(ModelProviderPreference preference);
}

internal sealed class DelegatingModelProviderPreferencePort : IModelProviderPreferencePort
{
    private readonly Func<ProjectId, ModelProviderId> _getPreferredModelProviderId;
    private readonly Func<ModelProviderId, bool> _isModelProviderReady;
    private readonly Action<PromptSessionBinding, ModelProviderPreference> _applyDraftPreference;
    private readonly Action<string, ModelProviderPreference> _applyThreadPreference;
    private readonly Action<ProjectId, ModelProviderPreference> _rememberProjectPreference;
    private readonly Action<string, ModelProviderPreference, bool> _rememberThreadPreference;
    private readonly Action<ModelProviderState> _applyDraftModelProviderState;
    private readonly Action<OpenThreadState> _applyThreadModelProviderState;
    private readonly Action<ModelProviderPreference> _rememberGlobalPreference;

    public DelegatingModelProviderPreferencePort(
        Func<ProjectId, ModelProviderId> getPreferredModelProviderId,
        Func<ModelProviderId, bool> isModelProviderReady,
        Action<PromptSessionBinding, ModelProviderPreference> applyDraftPreference,
        Action<string, ModelProviderPreference> applyThreadPreference,
        Action<ProjectId, ModelProviderPreference> rememberProjectPreference,
        Action<string, ModelProviderPreference, bool> rememberThreadPreference,
        Action<ModelProviderState>? applyDraftModelProviderState = null,
        Action<OpenThreadState>? applyThreadModelProviderState = null,
        Action<ModelProviderPreference>? rememberGlobalPreference = null)
    {
        ArgumentNullException.ThrowIfNull(getPreferredModelProviderId);
        ArgumentNullException.ThrowIfNull(isModelProviderReady);
        ArgumentNullException.ThrowIfNull(applyDraftPreference);
        ArgumentNullException.ThrowIfNull(applyThreadPreference);
        ArgumentNullException.ThrowIfNull(rememberProjectPreference);
        ArgumentNullException.ThrowIfNull(rememberThreadPreference);

        _getPreferredModelProviderId = getPreferredModelProviderId;
        _isModelProviderReady = isModelProviderReady;
        _applyDraftPreference = applyDraftPreference;
        _applyThreadPreference = applyThreadPreference;
        _rememberProjectPreference = rememberProjectPreference;
        _rememberThreadPreference = rememberThreadPreference;
        _applyDraftModelProviderState = applyDraftModelProviderState ?? (static _ => { });
        _applyThreadModelProviderState = applyThreadModelProviderState ?? (static _ => { });
        _rememberGlobalPreference = rememberGlobalPreference ?? (static _ => { });
    }

    public ModelProviderId GetPreferredModelProviderId(ProjectId projectId)
    {
        if (projectId == default)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        }

        return _getPreferredModelProviderId(projectId);
    }

    public bool IsModelProviderReady(ModelProviderId modelProviderId)
    {
        EnsureModelProviderId(modelProviderId);
        return _isModelProviderReady(modelProviderId);
    }

    public void ApplyDraftPreference(PromptSessionBinding promptSession, ModelProviderPreference preference)
    {
        ArgumentNullException.ThrowIfNull(promptSession);
        EnsurePreference(preference);
        _applyDraftPreference(promptSession, preference.Normalize());
    }

    public void ApplyThreadPreference(string threadId, ModelProviderPreference preference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        EnsurePreference(preference);
        _applyThreadPreference(threadId, preference.Normalize());
    }

    public void RememberProjectPreference(ProjectId projectId, ModelProviderPreference preference)
    {
        if (projectId == default)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        }

        EnsurePreference(preference);
        _rememberProjectPreference(projectId, preference.Normalize());
    }

    public void RememberThreadPreference(string threadId, ModelProviderPreference preference, bool persistNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        EnsurePreference(preference);
        _rememberThreadPreference(threadId, preference.Normalize(), persistNow);
    }

    public void ApplyDraftModelProviderState(ModelProviderState modelProviderState)
    {
        ArgumentNullException.ThrowIfNull(modelProviderState);
        _applyDraftModelProviderState(modelProviderState);
    }

    public void ApplyThreadModelProviderState(OpenThreadState threadState)
    {
        ArgumentNullException.ThrowIfNull(threadState);
        _applyThreadModelProviderState(threadState);
    }

    public void RememberGlobalPreference(ModelProviderPreference preference)
    {
        EnsurePreference(preference);
        _rememberGlobalPreference(preference.Normalize());
    }

    private static void EnsurePreference(ModelProviderPreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        EnsureModelProviderId(preference.ModelProviderId);
    }

    private static void EnsureModelProviderId(ModelProviderId modelProviderId)
    {
        if (modelProviderId.IsEmpty)
        {
            throw new ArgumentException("Model provider id cannot be empty.", nameof(modelProviderId));
        }
    }
}

internal sealed class FrontendModelProviderPreferencePort : IModelProviderPreferencePort
{
    private readonly Action<ModelProviderState> _applyDraftModelProviderState;
    private readonly Action<OpenThreadState> _applyThreadModelProviderState;
    private readonly Action<ModelProviderId, string?, AgentReasoningEffort?> _rememberGlobalPreference;
    private readonly Action<string, string?, AgentReasoningEffort?, bool> _rememberThreadPreference;

    public FrontendModelProviderPreferencePort(
        Action<ModelProviderState> applyDraftModelProviderState,
        Action<OpenThreadState> applyThreadModelProviderState,
        Action<ModelProviderId, string?, AgentReasoningEffort?> rememberGlobalPreference,
        Action<string, string?, AgentReasoningEffort?, bool> rememberThreadPreference)
    {
        ArgumentNullException.ThrowIfNull(applyDraftModelProviderState);
        ArgumentNullException.ThrowIfNull(applyThreadModelProviderState);
        ArgumentNullException.ThrowIfNull(rememberGlobalPreference);
        ArgumentNullException.ThrowIfNull(rememberThreadPreference);

        _applyDraftModelProviderState = applyDraftModelProviderState;
        _applyThreadModelProviderState = applyThreadModelProviderState;
        _rememberGlobalPreference = rememberGlobalPreference;
        _rememberThreadPreference = rememberThreadPreference;
    }

    public ModelProviderId GetPreferredModelProviderId(ProjectId projectId)
        => throw new NotSupportedException("The frontend preference adapter does not own project preference lookup yet.");

    public bool IsModelProviderReady(ModelProviderId modelProviderId)
        => throw new NotSupportedException("The frontend preference adapter does not own provider readiness lookup yet.");

    public void ApplyDraftPreference(PromptSessionBinding promptSession, ModelProviderPreference preference)
        => ApplyDraftModelProviderState(CreateLegacyModelProviderState(preference));

    public void ApplyThreadPreference(string threadId, ModelProviderPreference preference)
        => throw new NotSupportedException("Applying session preference still requires the open session projection during frontend migration.");

    public void RememberProjectPreference(ProjectId projectId, ModelProviderPreference preference)
        => RememberGlobalPreference(preference);

    public void RememberThreadPreference(string threadId, ModelProviderPreference preference, bool persistNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        EnsurePreference(preference);
        var normalized = preference.Normalize();
        _rememberThreadPreference(threadId, normalized.ModelId, normalized.ReasoningEffort, persistNow);
    }

    public void ApplyDraftModelProviderState(ModelProviderState modelProviderState)
    {
        ArgumentNullException.ThrowIfNull(modelProviderState);
        _applyDraftModelProviderState(modelProviderState);
    }

    public void ApplyThreadModelProviderState(OpenThreadState threadState)
    {
        ArgumentNullException.ThrowIfNull(threadState);
        _applyThreadModelProviderState(threadState);
    }

    public void RememberGlobalPreference(ModelProviderPreference preference)
    {
        EnsurePreference(preference);
        var normalized = preference.Normalize();
        _rememberGlobalPreference(normalized.ModelProviderId, normalized.ModelId, normalized.ReasoningEffort);
    }

    private static ModelProviderState CreateLegacyModelProviderState(ModelProviderPreference preference)
    {
        EnsurePreference(preference);
        return new ModelProviderState(preference.ModelProviderId, preference.ModelProviderId.Value)
        {
            SelectedModelId = preference.ModelId,
            SelectedReasoningEffort = preference.ReasoningEffort,
        };
    }

    private static void EnsurePreference(ModelProviderPreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        if (preference.ModelProviderId.IsEmpty)
        {
            throw new ArgumentException("Model provider id cannot be empty.", nameof(preference));
        }
    }
}
