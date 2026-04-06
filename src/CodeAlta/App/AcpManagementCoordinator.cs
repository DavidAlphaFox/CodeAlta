using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;

namespace CodeAlta.App;

internal sealed class AcpManagementCoordinator
{
    private readonly AcpManagementService _service;
    private readonly Func<Task> _reloadAcpBackendsAsync;
    private readonly Func<string, Task> _probeAcpBackendAsync;
    private readonly Func<Rectangle?> _getBounds;
    private readonly Func<Visual?> _getFocusTarget;

    public AcpManagementCoordinator(
        AcpManagementService service,
        Func<Task> reloadAcpBackendsAsync,
        Func<string, Task> probeAcpBackendAsync,
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(reloadAcpBackendsAsync);
        ArgumentNullException.ThrowIfNull(probeAcpBackendAsync);
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);

        _service = service;
        _reloadAcpBackendsAsync = reloadAcpBackendsAsync;
        _probeAcpBackendAsync = probeAcpBackendAsync;
        _getBounds = getBounds;
        _getFocusTarget = getFocusTarget;
    }

    public void Open()
    {
        new AcpManagementDialog(_service, _reloadAcpBackendsAsync, _probeAcpBackendAsync, _getBounds, _getFocusTarget).Show();
    }
}
