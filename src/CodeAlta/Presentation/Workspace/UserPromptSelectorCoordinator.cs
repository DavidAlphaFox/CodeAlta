using CodeAlta.App;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime.SystemPrompts;
using CodeAlta.Presentation.Chat;
using CodeAlta.ViewModels;

namespace CodeAlta.Presentation.Workspace;

internal sealed class UserPromptSelectorCoordinator
{
    private readonly SessionWorkspaceViewModel _workspaceViewModel;
    private readonly CatalogOptions _catalogOptions;
    private readonly SessionSelectionContext _sessionSelection;
    private readonly UserPromptPreferenceCoordinator _preferences;
    private readonly WorkspaceRefreshContext _workspaceRefresh;
    private readonly UserPromptCatalog _promptCatalog;
    private readonly Func<SessionViewViewState> _getViewState;
    private readonly Action _persistViewState;
    private readonly Action<SessionViewDescriptor> _persistSessionLocalState;
    private readonly Action _syncUserPromptSelectorItems;
    private readonly Action<string, bool, StatusTone> _setStatus;
    private bool _selectorsRefreshing;

    public UserPromptSelectorCoordinator(
        SessionWorkspaceViewModel workspaceViewModel,
        CatalogOptions catalogOptions,
        SessionSelectionContext sessionSelection,
        UserPromptPreferenceCoordinator preferences,
        WorkspaceRefreshContext workspaceRefresh,
        Func<SessionViewViewState> getViewState,
        Action persistViewState,
        Action<SessionViewDescriptor> persistSessionLocalState,
        Action syncUserPromptSelectorItems,
        Action<string, bool, StatusTone> setStatus,
        UserPromptCatalog? promptCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceViewModel);
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(sessionSelection);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(workspaceRefresh);
        ArgumentNullException.ThrowIfNull(getViewState);
        ArgumentNullException.ThrowIfNull(persistViewState);
        ArgumentNullException.ThrowIfNull(persistSessionLocalState);
        ArgumentNullException.ThrowIfNull(syncUserPromptSelectorItems);
        ArgumentNullException.ThrowIfNull(setStatus);

        _workspaceViewModel = workspaceViewModel;
        _catalogOptions = catalogOptions;
        _sessionSelection = sessionSelection;
        _preferences = preferences;
        _workspaceRefresh = workspaceRefresh;
        _getViewState = getViewState;
        _persistViewState = persistViewState;
        _persistSessionLocalState = persistSessionLocalState;
        _syncUserPromptSelectorItems = syncUserPromptSelectorItems;
        _setStatus = setStatus;
        _promptCatalog = promptCatalog ?? new UserPromptCatalog();
    }

    public void RefreshForDraftScope(string? preferredPromptName = null)
    {
        _selectorsRefreshing = true;
        try
        {
            var project = _sessionSelection.GetSelectedProject();
            var prompts = LoadPromptOptions(project);
            if (prompts.Count == 0)
            {
                SetPromptSelection([], -1, canSelect: false);
                return;
            }

            var viewState = _getViewState();
            var preferred = NormalizeOptionalText(preferredPromptName)
                ?? _preferences.GetDraftUserPromptName(viewState, project?.ProjectPath, project?.Id)
                ?? UserPromptCatalog.DefaultPromptName;
            var selectedIndex = FindPromptIndex(prompts, preferred);
            SetPromptSelection(prompts, selectedIndex, canSelect: true);
            if (preferredPromptName is not null)
            {
                _preferences.RememberDraftUserPromptName(viewState, prompts[selectedIndex].PromptName, project?.ProjectPath, project?.Id);
                _persistViewState();
            }
        }
        finally
        {
            _selectorsRefreshing = false;
        }
    }

    public void RefreshForSession(OpenSessionState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _selectorsRefreshing = true;
        try
        {
            _preferences.ApplySessionUserPromptName(tab, _getViewState());
            var project = _sessionSelection.GetProjectById(tab.SessionView.ProjectRef);
            var prompts = LoadPromptOptions(project);
            if (prompts.Count == 0)
            {
                SetPromptSelection([], -1, canSelect: false);
                return;
            }

            var selectedIndex = FindPromptIndex(prompts, tab.UserPromptName ?? UserPromptCatalog.DefaultPromptName);
            var selectedPromptName = prompts[selectedIndex].PromptName;
            tab.UserPromptName = selectedPromptName;
            tab.SessionView.UserPromptName = selectedPromptName;
            SetPromptSelection(prompts, selectedIndex, canSelect: true);
        }
        finally
        {
            _selectorsRefreshing = false;
        }
    }

    public void OnPromptSelectionChanged(int newIndex)
    {
        if (_selectorsRefreshing)
        {
            return;
        }

        var options = _workspaceViewModel.UserPromptOptions;
        if ((uint)newIndex >= (uint)options.Count)
        {
            return;
        }

        var selectedPrompt = options[newIndex];
        _workspaceViewModel.SelectedUserPromptIndex = newIndex;
        var session = _sessionSelection.GetSelectedSession();
        if (session is null)
        {
            var project = _sessionSelection.GetSelectedProject();
            _preferences.RememberDraftUserPromptName(_getViewState(), selectedPrompt.PromptName, project?.ProjectPath, project?.Id);
            _persistViewState();
            _workspaceRefresh.ApplySessionUsageProjection();
            _setStatus($"Selected prompt '{selectedPrompt.Label}'.", false, StatusTone.Ready);
            return;
        }

        var tab = _sessionSelection.EnsureSessionTab(session);
        _preferences.RememberSessionUserPromptName(_getViewState(), tab, selectedPrompt.PromptName);
        _persistViewState();
        _persistSessionLocalState(tab.SessionView);
        _workspaceRefresh.ApplyHeaderProjection();
        _setStatus($"Selected prompt '{selectedPrompt.Label}'.", false, StatusTone.Ready);
    }

    public string? GetPreferredUserPromptName()
    {
        if (_workspaceViewModel.SelectedUserPromptIndex is var index &&
            (uint)index < (uint)_workspaceViewModel.UserPromptOptions.Count)
        {
            return _workspaceViewModel.UserPromptOptions[index].PromptName;
        }

        var session = _sessionSelection.GetSelectedSession();
        if (session is not null)
        {
            return _sessionSelection.FindOpenSession(session.SessionId)?.UserPromptName ?? session.UserPromptName;
        }

        var project = _sessionSelection.GetSelectedProject();
        return _preferences.GetDraftUserPromptName(_getViewState(), project?.ProjectPath, project?.Id);
    }

    public void RefreshPrompts()
    {
        if (_sessionSelection.GetSelectedSession() is { } session && _sessionSelection.FindOpenSession(session.SessionId) is { } tab)
        {
            RefreshForSession(tab);
        }
        else
        {
            RefreshForDraftScope();
        }
    }

    private IReadOnlyList<UserPromptOption> LoadPromptOptions(ProjectDescriptor? project)
    {
        var prompts = _promptCatalog.ListEffectivePrompts(new UserPromptCatalogQuery
        {
            UserProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UserCodeAltaRoot = _catalogOptions.GlobalRoot,
            ProjectRoot = project?.ProjectPath,
            ProjectPromptResourcesTrusted = project is not null,
        });
        return UserPromptPresentation.BuildPromptOptions(prompts);
    }

    private void SetPromptSelection(IReadOnlyList<UserPromptOption> prompts, int selectedIndex, bool canSelect)
    {
        _workspaceViewModel.UserPromptOptions = prompts;
        _workspaceViewModel.SelectedUserPromptIndex = selectedIndex;
        _workspaceViewModel.CanSelectUserPrompt = canSelect;
        _syncUserPromptSelectorItems();
    }

    private static int FindPromptIndex(IReadOnlyList<UserPromptOption> prompts, string? preferredPromptName)
    {
        if (prompts.Count == 0)
        {
            return -1;
        }

        var preferred = NormalizeOptionalText(preferredPromptName) ?? UserPromptCatalog.DefaultPromptName;
        var index = prompts.ToList().FindIndex(prompt => string.Equals(prompt.PromptName, preferred, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            return index;
        }

        index = prompts.ToList().FindIndex(static prompt => string.Equals(prompt.PromptName, UserPromptCatalog.DefaultPromptName, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : 0;
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
