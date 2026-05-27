using CodeAlta.App;
using CodeAlta.ViewModels;

namespace CodeAlta.Frontend.Commands;

internal static class ShellCommandSurfaceComposition
{
    public static ShellCommandSurfaceCoordinator Create(
        PromptComposerViewModel promptComposerViewModel,
        SessionWorkspaceViewModel sessionWorkspaceViewModel,
        SessionCommandCoordinator sessionCommandCoordinator,
        IShellPromptInputService promptInputService,
        IShellSessionCommandService sessionCommandService,
        IShellDialogCommandService dialogCommandService,
        IShellNavigationCommandService navigationCommandService,
        IShellTabCommandService tabCommandService,
        IShellStatusService statusService,
        IPluginCommandService pluginCommandService,
        Action toggleCommandBarMultiLine)
    {
        ArgumentNullException.ThrowIfNull(promptComposerViewModel);
        ArgumentNullException.ThrowIfNull(sessionWorkspaceViewModel);
        ArgumentNullException.ThrowIfNull(sessionCommandCoordinator);
        ArgumentNullException.ThrowIfNull(promptInputService);
        ArgumentNullException.ThrowIfNull(sessionCommandService);
        ArgumentNullException.ThrowIfNull(dialogCommandService);
        ArgumentNullException.ThrowIfNull(navigationCommandService);
        ArgumentNullException.ThrowIfNull(tabCommandService);
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(pluginCommandService);
        ArgumentNullException.ThrowIfNull(toggleCommandBarMultiLine);

        var commandPalettePresenter = new ShellCommandPalettePresenter(dialogCommandService);
        var shellCommandRegistry = new ShellCommandRegistryFactory(
            sessionCommandCoordinator,
            dialogCommandService,
            navigationCommandService,
            tabCommandService,
            statusService,
            pluginCommandService).Create(commandPalettePresenter);
        var shellCommandDispatcher = new ShellCommandDispatcher(shellCommandRegistry);
        var shellCommandBindingProjector = new ShellCommandBindingProjector(
            promptComposerViewModel,
            sessionWorkspaceViewModel,
            sessionCommandService,
            statusService,
            shellCommandRegistry,
            shellCommandDispatcher,
            pluginCommandService);
        return new ShellCommandSurfaceCoordinator(
            promptInputService,
            shellCommandDispatcher,
            shellCommandBindingProjector,
            commandPalettePresenter,
            toggleCommandBarMultiLine);
    }
}
