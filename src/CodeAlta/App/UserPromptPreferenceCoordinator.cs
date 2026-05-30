using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime.SystemPrompts;

namespace CodeAlta.App;

internal sealed class UserPromptPreferenceCoordinator
{
    private readonly Dictionary<string, string> _draftPromptNamesByScope = new(StringComparer.OrdinalIgnoreCase);

    public string? GetDraftUserPromptName(SessionViewViewState viewState, string? draftProjectRoot, string? draftProjectId)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        if (_draftPromptNamesByScope.TryGetValue(BuildDraftScopeKey(draftProjectRoot), out var draftPromptName))
        {
            return draftPromptName;
        }

        return viewState.ProjectPreferences.TryGetValue(BuildProjectPreferenceKey(draftProjectId), out var preference)
            ? NormalizeOptionalText(preference.UserPromptName)
            : null;
    }

    public void RememberDraftUserPromptName(SessionViewViewState viewState, string promptName, string? draftProjectRoot, string? draftProjectId)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptName);
        var normalizedPromptName = promptName.Trim();
        _draftPromptNamesByScope[BuildDraftScopeKey(draftProjectRoot)] = normalizedPromptName;
        var preferenceKey = BuildProjectPreferenceKey(draftProjectId);
        viewState.ProjectPreferences.TryGetValue(preferenceKey, out var existingPreference);
        viewState.ProjectPreferences[preferenceKey] = new SessionViewPreference
        {
            ProviderKey = existingPreference?.ProviderKey,
            ModelId = existingPreference?.ModelId,
            UserPromptName = normalizedPromptName,
            ReasoningEffort = existingPreference?.ReasoningEffort,
        };
        viewState.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApplySessionUserPromptName(OpenSessionState tab, SessionViewViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(viewState);
        viewState.SessionPreferences.TryGetValue(tab.SessionView.SessionId, out var persistedPreference);
        tab.UserPromptName = NormalizeOptionalText(tab.UserPromptName)
            ?? NormalizeOptionalText(tab.SessionView.UserPromptName)
            ?? NormalizeOptionalText(persistedPreference?.UserPromptName)
            ?? UserPromptCatalog.DefaultPromptName;
        tab.SessionView.UserPromptName = tab.UserPromptName;
    }

    public void RememberSessionUserPromptName(SessionViewViewState viewState, OpenSessionState tab, string promptName)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptName);
        var normalizedPromptName = promptName.Trim();
        viewState.SessionPreferences.TryGetValue(tab.SessionView.SessionId, out var existingPreference);
        viewState.SessionPreferences[tab.SessionView.SessionId] = new SessionViewPreference
        {
            ProviderKey = existingPreference?.ProviderKey,
            ModelId = existingPreference?.ModelId ?? tab.ModelId,
            UserPromptName = normalizedPromptName,
            ReasoningEffort = existingPreference?.ReasoningEffort ?? tab.ReasoningEffort,
        };
        tab.UserPromptName = normalizedPromptName;
        tab.SessionView.UserPromptName = normalizedPromptName;
        viewState.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string BuildDraftScopeKey(string? draftProjectRoot)
        => string.IsNullOrWhiteSpace(draftProjectRoot) ? "__global__" : draftProjectRoot.Trim();

    private static string BuildProjectPreferenceKey(string? projectId)
        => string.IsNullOrWhiteSpace(projectId) ? ModelProviderPreferenceCoordinator.GlobalProjectPreferenceKey : projectId.Trim();

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
