using CodeAlta.Agent;
using CodeAlta.Catalog;
using XenoAtom.Logging;

internal sealed partial class CodeAltaTerminalUi
{
    private void LoadConfigState()
    {
        _globalConfig = _configStore.LoadGlobal();
        _projectConfigCache.Clear();
    }

    private CodeAltaConfigDocument GetProjectConfig(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return new CodeAltaConfigDocument();
        }

        if (_projectConfigCache.TryGetValue(projectRoot, out var existing))
        {
            return existing;
        }

        var config = _configStore.LoadProject(projectRoot);
        _projectConfigCache[projectRoot] = config;
        return config;
    }

    private CodeAltaBackendPreference GetEffectiveBackendPreference(AgentBackendId backendId, string? projectRoot)
    {
        var global = GetBackendSettings(_globalConfig, backendId);
        var project = string.IsNullOrWhiteSpace(projectRoot)
            ? null
            : GetBackendSettings(GetProjectConfig(projectRoot), backendId);

        var model = NormalizeModel(project?.Model) ?? NormalizeModel(global?.Model);
        var reasoningEffort = ParseReasoningEffort(project?.ReasoningEffort) ?? ParseReasoningEffort(global?.ReasoningEffort);
        return new CodeAltaBackendPreference(model, reasoningEffort);
    }

    private string BuildDraftScopeKey()
        => GetDraftProjectRoot() ?? "__global__";

    private string? GetDraftProjectRoot()
        => _globalScopeSelected ? null : GetSelectedProject()?.ProjectPath;

    private string? GetThreadProjectRoot(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return GetProjectById(thread.ProjectRef)?.ProjectPath;
    }

    private void ApplyDraftBackendPreference(ChatBackendState backendState)
    {
        ArgumentNullException.ThrowIfNull(backendState);

        var scopeKey = BuildDraftScopeKey();
        var defaults = GetEffectiveBackendPreference(backendState.BackendId, GetDraftProjectRoot());
        var preserveCurrentSelection = string.Equals(backendState.DraftScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase);
        var preferredModelId = preserveCurrentSelection
            ? backendState.SelectedModelId ?? defaults.Model
            : defaults.Model;

        backendState.SelectedModelId = ResolvePreferredModelId(backendState.Models, preferredModelId);
        var selectedModel = FindModel(backendState.Models, backendState.SelectedModelId);
        var preferredReasoningEffort = preserveCurrentSelection
            ? backendState.SelectedReasoningEffort ?? defaults.ReasoningEffort
            : defaults.ReasoningEffort;

        backendState.SelectedReasoningEffort = ResolvePreferredReasoningEffort(selectedModel, preferredReasoningEffort);
        backendState.DraftScopeKey = scopeKey;
    }

    private void ApplyThreadPreference(ThreadTabState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        _viewState.ThreadPreferences.TryGetValue(tab.Thread.ThreadId, out var persistedPreference);
        var defaults = GetEffectiveBackendPreference(tab.BackendId, GetThreadProjectRoot(tab.Thread));
        tab.ModelId ??= persistedPreference?.ModelId ?? defaults.Model;
        tab.ReasoningEffort ??= persistedPreference?.ReasoningEffort ?? defaults.ReasoningEffort;
        tab.AutoScroll = persistedPreference?.AutoScroll ?? true;

        if (!_chatBackendStates.TryGetValue(tab.BackendId.Value, out var backendState))
        {
            return;
        }

        tab.ModelId = ResolvePreferredModelId(
            backendState.Models,
            tab.ModelId);

        var selectedModel = FindModel(backendState.Models, tab.ModelId);
        tab.ReasoningEffort = ResolvePreferredReasoningEffort(
            selectedModel,
            tab.ReasoningEffort);
    }

    private void RememberGlobalBackendPreference(
        AgentBackendId backendId,
        string? modelId,
        AgentReasoningEffort? reasoningEffort)
    {
        try
        {
            _configStore.SaveGlobalBackendPreference(backendId, modelId, reasoningEffort);

            var normalizedModel = NormalizeModel(modelId);
            var normalizedReasoning = FormatReasoningEffort(reasoningEffort);
            if (normalizedModel is null && normalizedReasoning is null)
            {
                _globalConfig.Backends.Remove(backendId.Value);
            }
            else
            {
                _globalConfig.Backends[backendId.Value] = new CodeAltaBackendSettingsDocument
                {
                    Model = normalizedModel,
                    ReasoningEffort = normalizedReasoning,
                };
            }
        }
        catch (Exception ex)
        {
            UiLogger.Error(ex, "Failed to save CodeAlta backend preferences.");
        }
    }

    private void RememberThreadPreference(
        string threadId,
        string? modelId,
        AgentReasoningEffort? reasoningEffort,
        bool autoScroll,
        bool persistNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var normalizedModel = string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();
        if (normalizedModel is null && reasoningEffort is null && autoScroll)
        {
            _viewState.ThreadPreferences.Remove(threadId);
        }
        else
        {
            _viewState.ThreadPreferences[threadId] = new WorkThreadPreference
            {
                ModelId = normalizedModel,
                ReasoningEffort = reasoningEffort,
                AutoScroll = autoScroll,
            };
        }

        _viewState.UpdatedAt = DateTimeOffset.UtcNow;
        if (persistNow)
        {
            _ = PersistViewStateAsync();
        }
    }

    private static AgentModelInfo? FindModel(IReadOnlyList<AgentModelInfo> models, string? modelId)
    {
        return string.IsNullOrWhiteSpace(modelId)
            ? null
            : models.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
    }

    private static string? NormalizeModel(string? modelId)
        => string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();

    private static AgentReasoningEffort? ParseReasoningEffort(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "none" => AgentReasoningEffort.None,
            "minimal" => AgentReasoningEffort.Minimal,
            "low" => AgentReasoningEffort.Low,
            "medium" => AgentReasoningEffort.Medium,
            "high" => AgentReasoningEffort.High,
            "xhigh" => AgentReasoningEffort.XHigh,
            _ => null,
        };
    }

    private static CodeAltaBackendSettingsDocument? GetBackendSettings(
        CodeAltaConfigDocument document,
        AgentBackendId backendId)
    {
        return document.Backends.TryGetValue(backendId.Value, out var settings)
            ? settings
            : null;
    }

    private static string? FormatReasoningEffort(AgentReasoningEffort? effort)
    {
        return effort switch
        {
            null => null,
            AgentReasoningEffort.None => "none",
            AgentReasoningEffort.Minimal => "minimal",
            AgentReasoningEffort.Low => "low",
            AgentReasoningEffort.Medium => "medium",
            AgentReasoningEffort.High => "high",
            AgentReasoningEffort.XHigh => "xhigh",
            _ => null,
        };
    }
}
