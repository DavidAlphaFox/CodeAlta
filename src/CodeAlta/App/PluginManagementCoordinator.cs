using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;

namespace CodeAlta.App;

internal sealed class PluginManagementCoordinator
{
    private readonly PluginManagementService _service;
    private readonly Func<Rectangle?> _getBounds;
    private readonly Func<Visual?> _getFocusTarget;

    public PluginManagementCoordinator(
        PluginManagementService service,
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
        => new PluginManagementDialog(_service, _getBounds, _getFocusTarget).Show();
}
