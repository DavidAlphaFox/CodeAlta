using XenoAtom.Terminal.UI;

namespace CodeAlta.Views;

internal sealed record SessionWorkspaceChromeController(
    Func<Visual> BuildSessionUsageIndicatorVisual,
    Func<Visual?>? BuildPluginSessionStatusVisual,
    Action<Visual> ToggleSessionInfoPopup,
    Action OpenModelProviders)
{
    public static SessionWorkspaceChromeController Create(
        Func<Visual> buildSessionUsageIndicatorVisual,
        Func<Visual?>? buildPluginSessionStatusVisual,
        Action<Visual> toggleSessionInfoPopup,
        Action openModelProviders)
    {
        ArgumentNullException.ThrowIfNull(buildSessionUsageIndicatorVisual);
        ArgumentNullException.ThrowIfNull(toggleSessionInfoPopup);
        ArgumentNullException.ThrowIfNull(openModelProviders);
        return new SessionWorkspaceChromeController(buildSessionUsageIndicatorVisual, buildPluginSessionStatusVisual, toggleSessionInfoPopup, openModelProviders);
    }

    public static SessionWorkspaceChromeController Empty { get; } = new(
        static () => new XenoAtom.Terminal.UI.Controls.TextBlock(string.Empty),
        null,
        static _ => { },
        static () => { });
}
