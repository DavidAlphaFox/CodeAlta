using CodeAlta.Frontend.Commands;
using XenoAtom.Terminal.UI;

namespace CodeAlta.Views;

internal static class CodeAltaGlobalCommandConfigurator
{
    public static void Configure(TerminalApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _ = app.RemoveGlobalCommand("TerminalApp.Quit");
        app.AddGlobalCommand(ShellCommandViewFactory.Create(
            ShellCommandCatalog.Get("CodeAlta.Shell.Exit"),
            app.Stop));
    }
}
