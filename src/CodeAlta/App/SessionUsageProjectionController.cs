using CodeAlta.Agent;
using CodeAlta.App.Context;
using CodeAlta.Models;
using CodeAlta.Presentation.Sidebar;
using CodeAlta.ViewModels;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using IntState = XenoAtom.Terminal.UI.State<int>;

namespace CodeAlta.App;

internal sealed class SessionUsageProjectionController
{
    private readonly SessionUsageViewModel _sessionUsageViewModel;
    private readonly Dictionary<string, ModelProviderState> _modelProviderStates;
    private readonly SessionSelectionContext _sessionSelection;
    private readonly ShellWorkspaceContext _workspaceContext;
    private readonly IntState _usageRefreshState;

    public SessionUsageProjectionController(
        SessionUsageViewModel sessionUsageViewModel,
        Dictionary<string, ModelProviderState> modelProviderStates,
        SessionSelectionContext sessionSelection,
        ShellWorkspaceContext workspaceContext,
        IntState usageRefreshState)
    {
        ArgumentNullException.ThrowIfNull(sessionUsageViewModel);
        ArgumentNullException.ThrowIfNull(modelProviderStates);
        ArgumentNullException.ThrowIfNull(sessionSelection);
        ArgumentNullException.ThrowIfNull(workspaceContext);
        ArgumentNullException.ThrowIfNull(usageRefreshState);

        _sessionUsageViewModel = sessionUsageViewModel;
        _modelProviderStates = modelProviderStates;
        _sessionSelection = sessionSelection;
        _workspaceContext = workspaceContext;
        _usageRefreshState = usageRefreshState;
    }

    public ComputedVisual CreateComputedVisual(Func<Visual> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return new ComputedVisual(
            () =>
            {
                var _ = _usageRefreshState.Value;
                return build();
            });
    }

    public void ApplySessionUsageProjection()
    {
        _workspaceContext.DispatchToUiDeferred(
            () =>
            {
                SyncSelectedSessionUsageViewModel();
                _usageRefreshState.Value++;
            });
    }

    public void Refresh()
    {
        SyncSelectedSessionUsageViewModel();
        _usageRefreshState.Value++;
    }

    private void SyncSelectedSessionUsageViewModel()
    {
        _workspaceContext.VerifyBindableAccess();
        if (_sessionSelection.Selection.Target is WorkspaceTarget.Session)
        {
            var selectedSession = _sessionSelection.GetSelectedSession();
            if (selectedSession is null)
            {
                return;
            }

            var tab = _sessionSelection.EnsureSessionTab(selectedSession);
            _modelProviderStates.TryGetValue(tab.ProviderId.Value, out var backendState);
            _sessionUsageViewModel.Usage = tab.Usage;
            _sessionUsageViewModel.ProviderName = ResolveProviderDisplayName(tab.ProviderId.Value, backendState);
            _sessionUsageViewModel.ModelName = tab.ModelId ?? backendState?.SelectedModelId;
            _sessionUsageViewModel.PluginTransientEvents = tab.PluginTransientEvents.Snapshot;
            return;
        }

        var providerId = _workspaceContext.GetPreferredModelProviderId();
        _modelProviderStates.TryGetValue(providerId.Value, out var draftBackendState);
        _sessionUsageViewModel.Usage = null;
        _sessionUsageViewModel.ProviderName = ResolveProviderDisplayName(providerId.Value, draftBackendState);
        _sessionUsageViewModel.ModelName = draftBackendState?.SelectedModelId;
        _sessionUsageViewModel.PluginTransientEvents = [];
    }

    private static string ResolveProviderDisplayName(string providerKey, ModelProviderState? backendState)
        => SidebarSessionPresentation.ResolveProviderDisplayName(providerKey, backendState?.DisplayName);
}
