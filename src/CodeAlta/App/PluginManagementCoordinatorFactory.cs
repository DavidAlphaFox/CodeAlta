using CodeAlta.Catalog;
using CodeAlta.Models;
using XenoAtom.Terminal.UI;

namespace CodeAlta.App;

internal static class PluginManagementCoordinatorFactory
{
    public static Func<Task> Create(CatalogOptions catalogOptions, Func<ProjectDescriptor?> getSelectedProject, Func<Visual?> getDialogAnchor, Action<string, bool, StatusTone> setStatus)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(getSelectedProject);
        ArgumentNullException.ThrowIfNull(getDialogAnchor);
        ArgumentNullException.ThrowIfNull(setStatus);
        var coordinator = new PluginManagementCoordinator(
            new PluginManagementService(catalogOptions, getSelectedProject),
            () => DialogBoundsResolver.ResolveAppBounds(getDialogAnchor()),
            getDialogAnchor);
        return () =>
        {
            coordinator.Open();
            return Task.CompletedTask;
        };
    }
}
