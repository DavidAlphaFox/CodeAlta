using CodeAlta.App.State;
using CodeAlta.Threading;
using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.App.Context;
using CodeAlta.App.Events;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;
using CodeAlta.Presentation.Prompting;
using CodeAlta.ViewModels;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Presentation.Workspace;

internal sealed class ModelProviderSelectorCoordinator : IPromptAvailabilityProjectionController
{
    private readonly IReadOnlyList<ModelProviderDescriptor> _providerDescriptors;
    private readonly SessionWorkspaceViewModel _workspaceViewModel;
    private readonly PromptComposerViewModel _promptComposerViewModel;
    private readonly Dictionary<string, ModelProviderState> _modelProviderStates;
    private readonly ModelProviderSelectorStateStore _selectorState;
    private readonly SessionSelectionContext _sessionSelection;
    private readonly IModelProviderPreferencePort _preferences;
    private readonly WorkspaceRefreshContext _workspaceRefresh;
    private readonly Func<string?, string?> _getEffectiveDefaultProviderKey;
    private readonly Action _syncModelProviderSelectorItems;
    private readonly Func<SessionViewDescriptor, OpenSessionState, bool> _canSelectSessionProvider;
    private readonly Func<SessionViewDescriptor, OpenSessionState, ModelProviderId, Task<bool>> _trySwitchSessionProviderAsync;
    private readonly Action _refreshSelectionAndSessionWorkspace;
    private readonly Func<IReadOnlyList<string>>? _getConfiguredProviderKeys;
    private readonly Func<ModelProviderPreference?> _getDraftModelProviderPreference;
    private readonly Func<IReadOnlyList<string>> _getPromptPlaceholderContributions;
    private readonly Dictionary<string, ModelProviderId> _draftProviderIdsByScope = new(StringComparer.OrdinalIgnoreCase);
    private ModelProviderId? _draftProviderId;
    private bool _selectorsRefreshing;

    public ModelProviderSelectorCoordinator(
        SessionWorkspaceViewModel workspaceViewModel,
        PromptComposerViewModel promptComposerViewModel,
        Dictionary<string, ModelProviderState> modelProviderStates,
        ModelProviderSelectorStateStore selectorState,
        SessionSelectionContext sessionSelection,
        IModelProviderPreferencePort preferences,
        WorkspaceRefreshContext workspaceRefresh,
        Func<string?, string?> getEffectiveDefaultProviderKey,
        Action syncModelProviderSelectorItems,
        Func<SessionViewDescriptor, OpenSessionState, bool>? canSelectSessionProvider = null,
        Func<SessionViewDescriptor, OpenSessionState, ModelProviderId, Task<bool>>? trySwitchSessionProviderAsync = null,
        Action? refreshSelectionAndSessionWorkspace = null,
        Func<IReadOnlyList<string>>? getConfiguredProviderKeys = null,
        Func<ModelProviderPreference?>? getDraftModelProviderPreference = null,
        Func<IReadOnlyList<string>>? getPromptPlaceholderContributions = null)
        : this(
            ModelProviderPresentation.CreateProviderStates().Values
                .Select(static state => new ModelProviderDescriptor(state.ProviderId, state.DisplayName))
                .ToArray(),
            workspaceViewModel,
            promptComposerViewModel,
            modelProviderStates,
            selectorState,
            sessionSelection,
            preferences,
            workspaceRefresh,
            getEffectiveDefaultProviderKey,
            syncModelProviderSelectorItems,
            canSelectSessionProvider,
            trySwitchSessionProviderAsync,
            refreshSelectionAndSessionWorkspace,
            getConfiguredProviderKeys,
            getDraftModelProviderPreference,
            getPromptPlaceholderContributions)
    {
    }

    public ModelProviderSelectorCoordinator(
        IReadOnlyList<ModelProviderDescriptor> providerDescriptors,
        SessionWorkspaceViewModel workspaceViewModel,
        PromptComposerViewModel promptComposerViewModel,
        Dictionary<string, ModelProviderState> modelProviderStates,
        ModelProviderSelectorStateStore selectorState,
        SessionSelectionContext sessionSelection,
        IModelProviderPreferencePort preferences,
        WorkspaceRefreshContext workspaceRefresh,
        Func<string?, string?> getEffectiveDefaultProviderKey,
        Action syncModelProviderSelectorItems,
        Func<SessionViewDescriptor, OpenSessionState, bool>? canSelectSessionProvider = null,
        Func<SessionViewDescriptor, OpenSessionState, ModelProviderId, Task<bool>>? trySwitchSessionProviderAsync = null,
        Action? refreshSelectionAndSessionWorkspace = null,
        Func<IReadOnlyList<string>>? getConfiguredProviderKeys = null,
        Func<ModelProviderPreference?>? getDraftModelProviderPreference = null,
        Func<IReadOnlyList<string>>? getPromptPlaceholderContributions = null)
    {
        ArgumentNullException.ThrowIfNull(providerDescriptors);
        ArgumentNullException.ThrowIfNull(workspaceViewModel);
        ArgumentNullException.ThrowIfNull(promptComposerViewModel);
        ArgumentNullException.ThrowIfNull(modelProviderStates);
        ArgumentNullException.ThrowIfNull(selectorState);
        ArgumentNullException.ThrowIfNull(sessionSelection);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(workspaceRefresh);
        ArgumentNullException.ThrowIfNull(getEffectiveDefaultProviderKey);
        ArgumentNullException.ThrowIfNull(syncModelProviderSelectorItems);

        _providerDescriptors = providerDescriptors;
        _workspaceViewModel = workspaceViewModel;
        _promptComposerViewModel = promptComposerViewModel;
        _modelProviderStates = modelProviderStates;
        _selectorState = selectorState;
        _sessionSelection = sessionSelection;
        _preferences = preferences;
        _workspaceRefresh = workspaceRefresh;
        _getEffectiveDefaultProviderKey = getEffectiveDefaultProviderKey;
        _syncModelProviderSelectorItems = syncModelProviderSelectorItems;
        _canSelectSessionProvider = canSelectSessionProvider ?? ((_, _) => false);
        _trySwitchSessionProviderAsync = trySwitchSessionProviderAsync ?? ((_, _, _) => Task.FromResult(false));
        _refreshSelectionAndSessionWorkspace = refreshSelectionAndSessionWorkspace ?? (() => { });
        _getConfiguredProviderKeys = getConfiguredProviderKeys;
        _getDraftModelProviderPreference = getDraftModelProviderPreference ?? (static () => null);
        _getPromptPlaceholderContributions = getPromptPlaceholderContributions ?? (static () => []);
    }

    public void RefreshForDraftScope(ModelProviderId? preferredProviderId = null)
    {
        _selectorState.VerifyBindableAccess();
        _selectorsRefreshing = true;
        try
        {
            var backendOptions = ModelProviderPresentation.BuildProviderOptions(_providerDescriptors);
            _workspaceViewModel.ProviderSummaryMarkup = ModelProviderPresentation.BuildProviderSummaryMarkup(
                _modelProviderStates.Values,
                isInitializing: false,
                configuredProviderKeys: GetConfiguredProviderKeys());
            if (backendOptions.Count == 0)
            {
                _selectorState.SetModelProviderSelection([], -1);
                _selectorState.SetModelSelection([], -1);
                _selectorState.SetReasoningSelection([], -1);
                _workspaceViewModel.CanSelectModelProvider = false;
                _workspaceViewModel.CanSelectModel = false;
                _workspaceViewModel.CanSelectReasoning = false;
                _syncModelProviderSelectorItems();
                return;
            }

            var providerId = preferredProviderId ?? GetPreferredDraftProviderId(backendOptions);
            var backendIndex = Math.Max(0, backendOptions.FindIndex(option => string.Equals(option.ProviderId.Value, providerId.Value, StringComparison.OrdinalIgnoreCase)));
            _draftProviderId = backendOptions[backendIndex].ProviderId;
            if (preferredProviderId is not null)
            {
                RememberDraftProviderForCurrentScope(_draftProviderId.Value);
            }

            _selectorState.SetModelProviderSelection(backendOptions, backendIndex);

            var backendState = _modelProviderStates[backendOptions[backendIndex].ProviderId.Value];
            _preferences.ApplyDraftModelProviderState(backendState);
            var modelOptions = ModelProviderPresentation.BuildModelOptions(backendState);
            _selectorState.SetModelSelection(
                modelOptions,
                Math.Clamp(
                modelOptions.FindIndex(option => string.Equals(option.ModelId, backendState.SelectedModelId, StringComparison.Ordinal)),
                0,
                Math.Max(0, modelOptions.Count - 1)));

            var selectedModel = backendState.Models.FirstOrDefault(model => string.Equals(model.Id, backendState.SelectedModelId, StringComparison.Ordinal))
                                ?? ModelProviderPresentation.GetSelectedModel(backendState);
            var reasoningOptions = ModelProviderPresentation.BuildReasoningOptions(selectedModel);
            _selectorState.SetReasoningSelection(
                reasoningOptions,
                Math.Clamp(
                reasoningOptions.FindIndex(option => option.Effort == backendState.SelectedReasoningEffort),
                0,
                Math.Max(0, reasoningOptions.Count - 1)));
            _workspaceViewModel.CanSelectModelProvider = true;
            _workspaceViewModel.CanSelectModel = backendState.Availability == ModelProviderAvailability.Ready;
            _workspaceViewModel.CanSelectReasoning = backendState.Availability == ModelProviderAvailability.Ready;
            _syncModelProviderSelectorItems();
        }
        finally
        {
            _selectorsRefreshing = false;
        }
    }

    public void RefreshForSession(OpenSessionState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        _selectorState.VerifyBindableAccess();
        _selectorsRefreshing = true;
        try
        {
            var configuredBackendOptions = ModelProviderPresentation.BuildProviderOptions(_providerDescriptors);
            var backendOptions = BuildSessionProviderOptions(tab, configuredBackendOptions);
            _workspaceViewModel.ProviderSummaryMarkup = ModelProviderPresentation.BuildProviderSummaryMarkup(
                _modelProviderStates.Values,
                isInitializing: false,
                configuredProviderKeys: GetConfiguredProviderKeys());
            if (backendOptions.Count == 0)
            {
                _selectorState.SetModelProviderSelection([], -1);
                _selectorState.SetModelSelection([], -1);
                _selectorState.SetReasoningSelection([], -1);
                _workspaceViewModel.CanSelectModelProvider = false;
                _workspaceViewModel.CanSelectModel = false;
                _workspaceViewModel.CanSelectReasoning = false;
                _syncModelProviderSelectorItems();
                return;
            }

            _selectorState.SetModelProviderSelection(
                backendOptions,
                Math.Clamp(
                backendOptions.FindIndex(option => string.Equals(option.ProviderId.Value, tab.ProviderId.Value, StringComparison.OrdinalIgnoreCase)),
                0,
                Math.Max(0, backendOptions.Count - 1)));

            var backendState = GetSessionProviderState(tab, out var backendStateIsRegistered);
            _preferences.ApplySessionModelProviderState(tab);

            var modelOptions = ModelProviderPresentation.BuildModelOptions(backendState, tab.ModelId);
            _selectorState.SetModelSelection(
                modelOptions,
                Math.Clamp(
                modelOptions.FindIndex(option => string.Equals(option.ModelId, tab.ModelId ?? backendState.SelectedModelId, StringComparison.Ordinal)),
                0,
                Math.Max(0, modelOptions.Count - 1)));

            var selectedModel = backendState.Models.FirstOrDefault(model =>
                                    string.Equals(model.Id, tab.ModelId, StringComparison.Ordinal))
                                ?? ModelProviderPresentation.GetSelectedModel(backendState);
            var reasoningOptions = ModelProviderPresentation.BuildReasoningOptions(selectedModel);
            _selectorState.SetReasoningSelection(
                reasoningOptions,
                Math.Clamp(
                reasoningOptions.FindIndex(option => option.Effort == tab.ReasoningEffort),
                0,
                Math.Max(0, reasoningOptions.Count - 1)));
            _workspaceViewModel.CanSelectModelProvider = HasRegisteredProviderOption(backendOptions) && _canSelectSessionProvider(tab.SessionView, tab);
            _workspaceViewModel.CanSelectModel = backendStateIsRegistered && backendState.Availability == ModelProviderAvailability.Ready;
            _workspaceViewModel.CanSelectReasoning = backendStateIsRegistered && backendState.Availability == ModelProviderAvailability.Ready;
            _syncModelProviderSelectorItems();
        }
        finally
        {
            _selectorsRefreshing = false;
        }
    }

    public async Task OnModelProviderSelectionChangedAsync(int newIndex)
    {
        if (_selectorsRefreshing)
        {
            return;
        }

        var options = GetCurrentModelProviderOptions();
        if (options.Count == 0)
        {
            return;
        }

        if ((uint)newIndex >= (uint)options.Count)
        {
            return;
        }

        _selectorState.SetSelectedModelProviderIndex(newIndex);

        var session = _sessionSelection.GetSelectedSession();
        if (session is null)
        {
            var selectedProviderId = options[newIndex].ProviderId;
            if (!_modelProviderStates.TryGetValue(selectedProviderId.Value, out var draftBackendState))
            {
                RefreshForDraftScope();
                return;
            }

            _draftProviderId = selectedProviderId;
            RememberDraftProviderForCurrentScope(selectedProviderId);
            RefreshForDraftScope(selectedProviderId);
            _preferences.RememberGlobalPreference(CreatePreference(
                selectedProviderId,
                draftBackendState.SelectedModelId,
                draftBackendState.SelectedReasoningEffort));
            _workspaceRefresh.ApplySessionUsageProjection();
            return;
        }

        var tab = _sessionSelection.EnsureSessionTab(session);
        var targetProviderId = options[newIndex].ProviderId;
        if (string.Equals(tab.ProviderId.Value, targetProviderId.Value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_modelProviderStates.ContainsKey(targetProviderId.Value))
        {
            RefreshForSession(tab);
            return;
        }

        if (!_canSelectSessionProvider(session, tab))
        {
            RefreshForSession(tab);
            return;
        }

        if (await _trySwitchSessionProviderAsync(session, tab, targetProviderId))
        {
            _refreshSelectionAndSessionWorkspace();
            return;
        }

        RefreshForSession(tab);
    }

    public void OnModelSelectionChanged(int newIndex)
    {
        if (_selectorsRefreshing)
        {
            return;
        }

        var session = _sessionSelection.GetSelectedSession();
        if (session is null)
        {
            var ProviderId = GetPreferredModelProviderId();
            var draftBackendState = _modelProviderStates[ProviderId.Value];
            var draftOptions = ModelProviderPresentation.BuildModelOptions(draftBackendState);
            if ((uint)newIndex >= (uint)draftOptions.Count)
            {
                return;
            }

            draftBackendState.SelectedModelId = draftOptions[newIndex].ModelId;
            var preferredModel = ModelProviderPreferenceCoordinator.FindModel(draftBackendState.Models, draftBackendState.SelectedModelId);
            draftBackendState.SelectedReasoningEffort = ModelProviderPresentation.ResolvePreferredReasoningEffort(preferredModel, preferredReasoningEffort: null);
            UpdateModelSelectorState(draftOptions, newIndex, preferredModel, draftBackendState.SelectedReasoningEffort);
            _preferences.RememberGlobalPreference(CreatePreference(ProviderId, draftBackendState.SelectedModelId, draftBackendState.SelectedReasoningEffort));
            _workspaceRefresh.ApplySessionUsageProjection();
            return;
        }

        var tab = _sessionSelection.EnsureSessionTab(session);
        if (!_modelProviderStates.TryGetValue(tab.ProviderId.Value, out var backendState))
        {
            RefreshForSession(tab);
            return;
        }

        var options = ModelProviderPresentation.BuildModelOptions(backendState, tab.ModelId);
        if ((uint)newIndex >= (uint)options.Count)
        {
            return;
        }

        tab.ModelId = options[newIndex].ModelId;
        var selectedModel = ModelProviderPreferenceCoordinator.FindModel(backendState.Models, tab.ModelId);
        tab.ReasoningEffort = ModelProviderPresentation.ResolvePreferredReasoningEffort(selectedModel, preferredReasoningEffort: null);
        UpdateModelSelectorState(options, newIndex, selectedModel, tab.ReasoningEffort);
        _preferences.RememberSessionPreference(tab.SessionView.SessionId, CreatePreference(new ModelProviderId(tab.ProviderId.Value), tab.ModelId, tab.ReasoningEffort), true);
        _preferences.RememberGlobalPreference(CreatePreference(new ModelProviderId(tab.ProviderId.Value), tab.ModelId, tab.ReasoningEffort));
        _workspaceRefresh.ApplyHeaderProjection();
    }

    public void OnReasoningSelectionChanged(int newIndex)
    {
        if (_selectorsRefreshing)
        {
            return;
        }

        var session = _sessionSelection.GetSelectedSession();
        if (session is null)
        {
            var ProviderId = GetPreferredModelProviderId();
            var draftBackendState = _modelProviderStates[ProviderId.Value];
            var draftSelectedModel = draftBackendState.Models.FirstOrDefault(model => string.Equals(model.Id, draftBackendState.SelectedModelId, StringComparison.Ordinal));
            var draftOptions = ModelProviderPresentation.BuildReasoningOptions(draftSelectedModel);
            if ((uint)newIndex >= (uint)draftOptions.Count)
            {
                return;
            }

            draftBackendState.SelectedReasoningEffort = draftOptions[newIndex].Effort;
            UpdateReasoningSelectorState(draftOptions, newIndex);
            _preferences.RememberGlobalPreference(CreatePreference(ProviderId, draftBackendState.SelectedModelId, draftBackendState.SelectedReasoningEffort));
            _workspaceRefresh.ApplySessionUsageProjection();
            return;
        }

        var tab = _sessionSelection.EnsureSessionTab(session);
        if (!_modelProviderStates.TryGetValue(tab.ProviderId.Value, out var backendState))
        {
            RefreshForSession(tab);
            return;
        }

        var selectedModel = backendState.Models.FirstOrDefault(model => string.Equals(model.Id, tab.ModelId, StringComparison.Ordinal));
        var options = ModelProviderPresentation.BuildReasoningOptions(selectedModel);
        if ((uint)newIndex >= (uint)options.Count)
        {
            return;
        }

        tab.ReasoningEffort = options[newIndex].Effort;
        UpdateReasoningSelectorState(options, newIndex);
        _preferences.RememberSessionPreference(tab.SessionView.SessionId, CreatePreference(new ModelProviderId(tab.ProviderId.Value), tab.ModelId, tab.ReasoningEffort), true);
        _preferences.RememberGlobalPreference(CreatePreference(new ModelProviderId(tab.ProviderId.Value), tab.ModelId, tab.ReasoningEffort));
    }

    public async Task<bool> SelectProviderModelAsync(ModelProviderId providerId, string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        if (!_modelProviderStates.TryGetValue(providerId.Value, out var backendState) ||
            backendState.Models.All(model => !string.Equals(model.Id, modelId, StringComparison.Ordinal)))
        {
            return false;
        }

        var session = _sessionSelection.GetSelectedSession();
        if (session is null)
        {
            SelectDraftProviderModel(providerId, backendState, modelId);
            return true;
        }

        var tab = _sessionSelection.EnsureSessionTab(session);
        if (!string.Equals(tab.ProviderId.Value, providerId.Value, StringComparison.OrdinalIgnoreCase))
        {
            if (!_canSelectSessionProvider(session, tab) ||
                !await _trySwitchSessionProviderAsync(session, tab, providerId))
            {
                RefreshForSession(tab);
                return false;
            }

            _refreshSelectionAndSessionWorkspace();
        }

        if (!_modelProviderStates.TryGetValue(tab.ProviderId.Value, out backendState))
        {
            return false;
        }

        SelectSessionProviderModel(tab, backendState, modelId);
        return true;
    }

    private void SelectDraftProviderModel(ModelProviderId providerId, ModelProviderState backendState, string modelId)
    {
        _draftProviderId = providerId;
        RememberDraftProviderForCurrentScope(providerId);
        backendState.SelectedModelId = modelId.Trim();
        var selectedModel = ModelProviderPreferenceCoordinator.FindModel(backendState.Models, backendState.SelectedModelId);
        backendState.SelectedReasoningEffort = ModelProviderPresentation.ResolvePreferredReasoningEffort(selectedModel, preferredReasoningEffort: null);
        _preferences.RememberGlobalPreference(CreatePreference(providerId, backendState.SelectedModelId, backendState.SelectedReasoningEffort));
        RefreshForDraftScope(providerId);
        _workspaceRefresh.ApplySessionUsageProjection();
    }

    private void SelectSessionProviderModel(OpenSessionState tab, ModelProviderState backendState, string modelId)
    {
        tab.ModelId = modelId.Trim();
        var selectedModel = ModelProviderPreferenceCoordinator.FindModel(backendState.Models, tab.ModelId);
        tab.ReasoningEffort = ModelProviderPresentation.ResolvePreferredReasoningEffort(selectedModel, preferredReasoningEffort: null);
        _preferences.RememberSessionPreference(tab.SessionView.SessionId, CreatePreference(new ModelProviderId(tab.ProviderId.Value), tab.ModelId, tab.ReasoningEffort), true);
        _preferences.RememberGlobalPreference(CreatePreference(new ModelProviderId(tab.ProviderId.Value), tab.ModelId, tab.ReasoningEffort));
        RefreshForSession(tab);
        _workspaceRefresh.ApplyHeaderProjection();
    }

    public ModelProviderId GetPreferredModelProviderId()
    {
        return UiDispatch.Invoke(
            _selectorState.GetUiDispatcher(),
            () =>
            {
                var options = ModelProviderPresentation.BuildProviderOptions(_providerDescriptors);
                if (options.Count == 0)
                {
                    return GetDefaultProviderId();
                }

                if (_selectorState.GetSelectedModelProviderIndex() is { } backendIndex &&
                    (uint)backendIndex < (uint)options.Count)
                {
                    return options[backendIndex].ProviderId;
                }

                if (ResolveConfiguredDefaultProviderId(options) is { } configuredDefaultProviderId)
                {
                    return configuredDefaultProviderId;
                }

                var readyBackend = options.FirstOrDefault(option => IsModelProviderReady(option.ProviderId));
                if (readyBackend is not null)
                {
                    return readyBackend.ProviderId;
                }

                return GetDefaultProviderId();
            });
    }

    public bool IsModelProviderReady(ModelProviderId providerId)
    {
        return _modelProviderStates.TryGetValue(providerId.Value, out var state) &&
               state.Availability == ModelProviderAvailability.Ready;
    }

    public bool TryGetPromptUnavailableStatus(out string message, out StatusTone tone)
    {
        var projection = BuildPromptComposerProjection();
        if (!projection.HasUnavailableStatus)
        {
            message = string.Empty;
            tone = StatusTone.Ready;
            return false;
        }

        message = projection.UnavailableStatusMessage!;
        tone = projection.UnavailableStatusTone;
        return true;
    }

    public void ApplyPromptAvailabilityProjection()
    {
        _selectorState.VerifyBindableAccess();
        var projection = BuildPromptComposerProjection();
        _promptComposerViewModel.Placeholder = projection.Placeholder;
        _promptComposerViewModel.IsEnabled = projection.IsEnabled;
        _promptComposerViewModel.CanSend = projection.CanSend;
        _promptComposerViewModel.CanSteer = projection.CanSteer;
        _promptComposerViewModel.CanAbort = projection.CanAbort;
        _promptComposerViewModel.CanCompact = projection.CanCompact;
        _promptComposerViewModel.CanCloseTab = projection.CanCloseTab;
        _promptComposerViewModel.CanClearQueue = projection.CanClearQueue;
        _promptComposerViewModel.CanAlwaysEnqueue = projection.CanAlwaysEnqueue;
        UpdateSessionModelProviderSelectionAvailability();
    }

    private void UpdateSessionModelProviderSelectionAvailability()
    {
        if (_sessionSelection.Selection.Target is not WorkspaceTarget.Session)
        {
            return;
        }

        var selectedSession = _sessionSelection.GetSelectedSession();
        if (selectedSession is null ||
            _sessionSelection.FindOpenSession(selectedSession.SessionId) is not { } selectedTab)
        {
            return;
        }

        _workspaceViewModel.CanSelectModelProvider = HasRegisteredProviderOption(_workspaceViewModel.ModelProviderOptions) &&
            _canSelectSessionProvider(selectedSession, selectedTab);
    }

    private IReadOnlyList<ModelProviderOption> GetCurrentModelProviderOptions()
        => _workspaceViewModel.ModelProviderOptions.Count > 0
            ? _workspaceViewModel.ModelProviderOptions
            : ModelProviderPresentation.BuildProviderOptions(_providerDescriptors);

    private List<ModelProviderOption> BuildSessionProviderOptions(
        OpenSessionState tab,
        IReadOnlyList<ModelProviderOption> configuredBackendOptions)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(configuredBackendOptions);

        var options = configuredBackendOptions.ToList();
        if (options.Any(option => string.Equals(option.ProviderId.Value, tab.ProviderId.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return options;
        }

        options.Insert(0, new ModelProviderOption(new ModelProviderId(tab.ProviderId.Value), BuildUnavailableSessionProviderLabel(tab.SessionView, new ModelProviderId(tab.ProviderId.Value))));
        return options;
    }

    private ModelProviderState GetSessionProviderState(OpenSessionState tab, out bool isRegistered)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (_modelProviderStates.TryGetValue(tab.ProviderId.Value, out var backendState))
        {
            isRegistered = true;
            return backendState;
        }

        isRegistered = false;
        return new ModelProviderState(new ModelProviderId(tab.ProviderId.Value), BuildUnavailableSessionProviderLabel(tab.SessionView, new ModelProviderId(tab.ProviderId.Value)))
        {
            Availability = ModelProviderAvailability.Unsupported,
            SelectedModelId = tab.ModelId,
            SelectedReasoningEffort = tab.ReasoningEffort,
        };
    }

    private bool HasRegisteredProviderOption(IReadOnlyList<ModelProviderOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Any(option => _modelProviderStates.ContainsKey(option.ProviderId.Value));
    }

    private ModelProviderId GetPreferredDraftProviderId(IReadOnlyList<ModelProviderOption> backendOptions)
    {
        if (_draftProviderIdsByScope.TryGetValue(GetCurrentDraftScopeKey(), out var scopedDraftProviderId) &&
            backendOptions.FirstOrDefault(option => string.Equals(option.ProviderId.Value, scopedDraftProviderId.Value, StringComparison.OrdinalIgnoreCase)) is { } scopedDraftBackend &&
            IsModelProviderReady(scopedDraftBackend.ProviderId))
        {
            return scopedDraftBackend.ProviderId;
        }

        if (_getDraftModelProviderPreference() is { } draftPreference &&
            backendOptions.FirstOrDefault(option =>
                string.Equals(option.ProviderId.Value, draftPreference.ModelProviderId.Value, StringComparison.OrdinalIgnoreCase)) is { } persistedDraftBackend)
        {
            return persistedDraftBackend.ProviderId;
        }

        if (_draftProviderId is { } draftProviderId &&
            backendOptions.FirstOrDefault(option => string.Equals(option.ProviderId.Value, draftProviderId.Value, StringComparison.OrdinalIgnoreCase)) is { } draftBackend)
        {
            if (IsModelProviderReady(draftBackend.ProviderId))
            {
                return draftBackend.ProviderId;
            }
        }

        if (ResolveConfiguredDefaultProviderId(backendOptions) is { } configuredDefaultProviderId)
        {
            return configuredDefaultProviderId;
        }

        var readyBackend = backendOptions.FirstOrDefault(option => IsModelProviderReady(option.ProviderId));
        if (readyBackend is not null)
        {
            return readyBackend.ProviderId;
        }

        return backendOptions.FirstOrDefault()?.ProviderId ?? GetDefaultProviderId();
    }

    private bool HasAnyReadyModelProvider()
    {
        return _modelProviderStates.Values.Any(static state => state.Availability == ModelProviderAvailability.Ready);
    }

    private void UpdateModelSelectorState(
        IReadOnlyList<ChatModelOption> modelOptions,
        int selectedModelIndex,
        AgentModelInfo? selectedModel,
        AgentReasoningEffort? selectedReasoningEffort)
    {
        ArgumentNullException.ThrowIfNull(modelOptions);

        var reasoningOptions = ModelProviderPresentation.BuildReasoningOptions(selectedModel);
        var selectedReasoningIndex = Math.Clamp(
            reasoningOptions.FindIndex(option => option.Effort == selectedReasoningEffort),
            0,
            Math.Max(0, reasoningOptions.Count - 1));

        var wasRefreshing = _selectorsRefreshing;
        _selectorsRefreshing = true;
        try
        {
            _selectorState.SetModelSelection(modelOptions, selectedModelIndex);
            _selectorState.SetReasoningSelection(reasoningOptions, selectedReasoningIndex);
            _syncModelProviderSelectorItems();
        }
        finally
        {
            _selectorsRefreshing = wasRefreshing;
        }
    }

    private void UpdateReasoningSelectorState(
        IReadOnlyList<ChatReasoningOption> reasoningOptions,
        int selectedReasoningIndex)
    {
        ArgumentNullException.ThrowIfNull(reasoningOptions);

        var wasRefreshing = _selectorsRefreshing;
        _selectorsRefreshing = true;
        try
        {
            _selectorState.SetReasoningSelection(reasoningOptions, selectedReasoningIndex);
            _syncModelProviderSelectorItems();
        }
        finally
        {
            _selectorsRefreshing = wasRefreshing;
        }
    }

    private PromptComposerProjection BuildPromptComposerProjection()
    {
        var selection = _sessionSelection.Selection;
        var selectedSession = selection.Target is WorkspaceTarget.Session ? _sessionSelection.GetSelectedSession() : null;
        var providerId = selectedSession is not null ? new ModelProviderId(selectedSession.ProviderId) : GetPreferredModelProviderId();
        if (!_modelProviderStates.TryGetValue(providerId.Value, out var backendState) && selectedSession is not null)
        {
            backendState = new ModelProviderState(providerId, BuildUnavailableSessionProviderLabel(selectedSession, providerId))
            {
                Availability = ModelProviderAvailability.Unsupported,
            };
        }

        if (backendState is null || string.IsNullOrWhiteSpace(backendState.DisplayName))
        {
            backendState = _modelProviderStates.Values.FirstOrDefault()
                ?? new ModelProviderState(GetDefaultProviderId(), GetDefaultProviderId().Value);
        }

        return PromptComposerProjectionBuilder.Build(
            selectedSession,
            _sessionSelection.GetSelectedProject(),
            selection.Target is WorkspaceTarget.Draft { IsGlobal: true },
            backendState.DisplayName,
            backendState.Availability,
            HasAnyReadyModelProvider(),
            _sessionSelection.HasOpenDraftTab(),
            _sessionSelection.OpenSessionIds.Count + (_sessionSelection.HasOpenDraftTab() ? 1 : 0),
            selectedSession?.SessionId,
            selectedSession is not null &&
            _sessionSelection.FindOpenSession(selectedSession.SessionId) is { } selectedTab &&
            selectedTab.QueuedPrompts.Count > 0,
            selectedSession is not null,
            selectedSession is not null &&
            _sessionSelection.FindOpenSession(selectedSession.SessionId) is { } selectedSessionTab &&
            selectedSession.StartedAt is not null &&
            !selectedSessionTab.StatusBusy,
            selectedSession is not null &&
            _sessionSelection.FindOpenSession(selectedSession.SessionId) is { } selectedAbortTab &&
            selectedAbortTab.StatusBusy,
            _getPromptPlaceholderContributions());
    }

    private ModelProviderId GetDefaultProviderId()
    {
        return _providerDescriptors.FirstOrDefault()?.ProviderId ?? ModelProviderIds.Codex;
    }

    private static ModelProviderPreference CreatePreference(
        ModelProviderId modelProviderId,
        string? modelId,
        AgentReasoningEffort? reasoningEffort)
        => new(modelProviderId, modelId, reasoningEffort);

    private static string BuildUnavailableSessionProviderLabel(SessionViewDescriptor session, ModelProviderId providerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var providerKey = string.IsNullOrWhiteSpace(session.ResolvedProviderKey)
            ? providerId.Value
            : session.ResolvedProviderKey.Trim();
        return $"{providerKey} (not configured)";
    }

    private IReadOnlyList<string>? GetConfiguredProviderKeys()
        => _getConfiguredProviderKeys?.Invoke();

    private ModelProviderId? ResolveConfiguredDefaultProviderId(IReadOnlyList<ModelProviderOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var projectRoot = _sessionSelection.GetSelectedProject()?.ProjectPath;
        var configuredProviderKey = _getEffectiveDefaultProviderKey(projectRoot);
        if (string.IsNullOrWhiteSpace(configuredProviderKey))
        {
            return null;
        }

        return options.FirstOrDefault(option =>
                string.Equals(option.ProviderId.Value, configuredProviderKey, StringComparison.OrdinalIgnoreCase))
            ?.ProviderId;
    }

    private void RememberDraftProviderForCurrentScope(ModelProviderId providerId)
        => _draftProviderIdsByScope[GetCurrentDraftScopeKey()] = providerId;

    private string GetCurrentDraftScopeKey()
        => _sessionSelection.GetSelectedProject()?.ProjectPath ?? "__global__";
}
