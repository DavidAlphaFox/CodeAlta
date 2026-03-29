using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Threading;
using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;

namespace CodeAlta.App;

internal sealed class NavigatorSettingsCoordinator
{
    private readonly ShellThreadStateCoordinator _threadStateCoordinator;
    private readonly Func<IUiDispatcher> _getUiDispatcher;
    private readonly Func<Rectangle?> _getDialogBounds;
    private readonly Func<Visual?> _getFocusTarget;
    private readonly Action _refreshCatalogAndThreadWorkspace;
    private readonly Action<string, bool, StatusTone> _setStatus;

    public NavigatorSettingsCoordinator(
        ShellThreadStateCoordinator threadStateCoordinator,
        Func<IUiDispatcher> getUiDispatcher,
        Func<Rectangle?> getDialogBounds,
        Func<Visual?> getFocusTarget,
        Action refreshCatalogAndThreadWorkspace,
        Action<string, bool, StatusTone> setStatus)
    {
        ArgumentNullException.ThrowIfNull(threadStateCoordinator);
        ArgumentNullException.ThrowIfNull(getUiDispatcher);
        ArgumentNullException.ThrowIfNull(getDialogBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);
        ArgumentNullException.ThrowIfNull(refreshCatalogAndThreadWorkspace);
        ArgumentNullException.ThrowIfNull(setStatus);

        _threadStateCoordinator = threadStateCoordinator;
        _getUiDispatcher = getUiDispatcher;
        _getDialogBounds = getDialogBounds;
        _getFocusTarget = getFocusTarget;
        _refreshCatalogAndThreadWorkspace = refreshCatalogAndThreadWorkspace;
        _setStatus = setStatus;
    }

    public void Open()
    {
        new NavigatorSettingsDialog(
            _threadStateCoordinator.GetNavigatorSettingsSnapshot(),
            SaveAsync,
            _getDialogBounds,
            _getFocusTarget)
            .Show();
    }

    private async Task SaveAsync(NavigatorSettings settings)
    {
        try
        {
            await _threadStateCoordinator.SaveNavigatorSettingsAsync(settings).ConfigureAwait(false);
            await _getUiDispatcher().InvokeAsync(_refreshCatalogAndThreadWorkspace).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _setStatus($"Failed to save navigator settings: {ex.Message}", false, StatusTone.Error);
        }
    }
}
