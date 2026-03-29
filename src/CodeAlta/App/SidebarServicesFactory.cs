using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Threading;
using CodeAlta.ViewModels;
using XenoAtom.Terminal.UI;

namespace CodeAlta.App;

internal static class SidebarServicesFactory
{
    public static (NavigatorActionCoordinator NavigatorActions, SidebarCoordinator Sidebar) Create(
        SidebarViewModel viewModel,
        CatalogOptions catalogOptions,
        CodeAltaShellController shellController,
        ShellThreadStateCoordinator threadStateCoordinator,
        Func<IUiDispatcher> getUiDispatcher,
        Action refreshCatalogAndThreadWorkspace,
        Action<string, bool, StatusTone> setStatus,
        Action setReadyStatusForCurrentSelection)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(shellController);
        ArgumentNullException.ThrowIfNull(threadStateCoordinator);
        ArgumentNullException.ThrowIfNull(getUiDispatcher);
        ArgumentNullException.ThrowIfNull(refreshCatalogAndThreadWorkspace);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(setReadyStatusForCurrentSelection);

        SidebarCoordinator? sidebar = null;
        var navigatorActions = new NavigatorActionCoordinator(
            shellController,
            threadStateCoordinator,
            getUiDispatcher,
            () => sidebar!.View.Tree.GetAbsoluteBounds(),
            () => sidebar!.View.Tree,
            setStatus,
            setReadyStatusForCurrentSelection);
        sidebar = new SidebarCoordinator(
            viewModel,
            catalogOptions,
            shellController,
            () => _ = ToggleSortModeAsync(threadStateCoordinator, refreshCatalogAndThreadWorkspace),
            () => setStatus("Navigator settings dialog will be added in the next step.", false, StatusTone.Info),
            navigatorActions.ConfirmDeleteThread,
            navigatorActions.ConfirmDeleteProject);
        return (navigatorActions, sidebar);
    }

    private static async Task ToggleSortModeAsync(
        ShellThreadStateCoordinator threadStateCoordinator,
        Action refreshCatalogAndThreadWorkspace)
    {
        var settings = threadStateCoordinator.GetNavigatorSettingsSnapshot();
        settings.SortMode = settings.SortMode == NavigatorProjectSortMode.Name
            ? NavigatorProjectSortMode.Date
            : NavigatorProjectSortMode.Name;
        await threadStateCoordinator.SaveNavigatorSettingsAsync(settings).ConfigureAwait(false);
        refreshCatalogAndThreadWorkspace();
    }
}
