using CodeAlta.Catalog;
using CodeAlta.Models;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;

namespace CodeAlta.App;

internal static class AcpManagementCoordinatorFactory
{
    public static AcpManagementCoordinator? Create(
        CodeAltaOwnedServices? ownedServices,
        CatalogOptions catalogOptions,
        Dictionary<string, ChatBackendState> chatBackendStates,
        Func<Task> reloadAcpBackendsAsync,
        Func<string, Task> probeAcpBackendAsync,
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(chatBackendStates);
        ArgumentNullException.ThrowIfNull(reloadAcpBackendsAsync);
        ArgumentNullException.ThrowIfNull(probeAcpBackendAsync);
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);

        return ownedServices is null
            ? null
            : new AcpManagementCoordinator(
                new AcpManagementService(
                    catalogOptions,
                    ownedServices.AcpAgentRegistryService,
                    new CodeAltaConfigStore(catalogOptions),
                    new AcpInstalledBackendStore(catalogOptions),
                    chatBackendStates),
                reloadAcpBackendsAsync,
                probeAcpBackendAsync,
                getBounds,
                getFocusTarget);
    }
}
