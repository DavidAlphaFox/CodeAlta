using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;

namespace CodeAlta.App;

internal sealed class AcpManagementCoordinator
{
    private readonly AcpManagementService _service;
    private readonly Func<Rectangle?> _getBounds;
    private readonly Func<Visual?> _getFocusTarget;

    public AcpManagementCoordinator(
        AcpManagementService service,
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);

        _service = service;
        _getBounds = getBounds;
        _getFocusTarget = getFocusTarget;
    }

    public void Open()
    {
        new AcpManagementDialog(_service, _getBounds, _getFocusTarget).Show();
    }
}
