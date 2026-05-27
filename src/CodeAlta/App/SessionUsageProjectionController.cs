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
    private readonly ThreadSelectionContext _threadSelection;
    private readonly ShellWorkspaceContext _workspaceContext;
    private readonly IntState _usageRefreshState;

    public SessionUsageProjectionController(
        SessionUsageViewModel sessionUsageViewModel,
        Dictionary<string, ModelProviderState> modelProviderStates,
        ThreadSelectionContext threadSelection,
        ShellWorkspaceContext workspaceContext,
        IntState usageRefreshState)
    {
        ArgumentNullException.ThrowIfNull(sessionUsageViewModel);
        ArgumentNullException.ThrowIfNull(modelProviderStates);
        ArgumentNullException.ThrowIfNull(threadSelection);
        ArgumentNullException.ThrowIfNull(workspaceContext);
        ArgumentNullException.ThrowIfNull(usageRefreshState);

        _sessionUsageViewModel = sessionUsageViewModel;
        _modelProviderStates = modelProviderStates;
        _threadSelection = threadSelection;
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
        if (_threadSelection.Selection.Target is WorkspaceTarget.Thread)
        {
            var selectedThread = _threadSelection.GetSelectedThread();
            if (selectedThread is null)
            {
                return;
            }

            var tab = _threadSelection.EnsureThreadTab(selectedThread);
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
        => SidebarThreadPresentation.ResolveProviderDisplayName(providerKey, backendState?.DisplayName);
}
