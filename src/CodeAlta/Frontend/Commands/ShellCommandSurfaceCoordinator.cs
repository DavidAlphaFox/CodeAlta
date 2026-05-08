using CodeAlta.App;
using CodeAlta.App.State;
using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Plugins.Abstractions;
using CodeAlta.ViewModels;
using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace CodeAlta.Frontend.Commands;

internal sealed class ShellCommandSurfaceCoordinator : IShellCommandSurfacePresenter
{
    internal static CommandPaletteStyle CommandPalettePopupStyle { get; } = CommandPaletteStyle.Default with
    {
        PopupWidthPercent = 50,
        MaxWidth = int.MaxValue,
        PopupHorizontalAlignment = Align.Center,
        PopupVerticalAlignment = Align.End,
        PopupOffsetY = -2,
    };
    internal static CommandPaletteStyle DialogCommandPalettePopupStyle { get; } = CommandPaletteStyle.Default with
    {
        PopupWidthPercent = 50,
        MaxWidth = int.MaxValue,
        PopupHorizontalAlignment = Align.Center,
        PopupVerticalAlignment = Align.Center,
    };

    private readonly PromptComposerViewModel _promptComposerViewModel;
    private readonly ThreadWorkspaceViewModel _threadWorkspaceViewModel;
    private readonly ThreadCommandCoordinator _threadCommandCoordinator;
    private readonly IShellThreadCommandService _threadCommandService;
    private readonly IShellDialogCommandService _dialogCommandService;
    private readonly IShellNavigationCommandService _navigationCommandService;
    private readonly IShellTabCommandService _tabCommandService;
    private readonly Action _toggleCommandBarMultiLine;
    private readonly IShellPromptInputService _promptInputService;
    private readonly IShellStatusService _statusService;
    private readonly ShellCommandRegistry _shellCommandRegistry;
    private readonly IShellCommandDispatcher _shellCommandDispatcher;
    private readonly ShellInputCoordinator _shellInputCoordinator;
    private readonly PluginHostBridge? _pluginHostBridge;
    private CommandPaletteStyle _activeCommandPaletteStyle = CommandPalettePopupStyle;
    private CommandPalette? _commandPalette;
    private ShellHelpDialog? _helpDialog;

    public ShellCommandSurfaceCoordinator(
        PromptComposerViewModel promptComposerViewModel,
        ThreadWorkspaceViewModel threadWorkspaceViewModel,
        ThreadCommandCoordinator threadCommandCoordinator,
        IShellPromptInputService promptInputService,
        IShellThreadCommandService threadCommandService,
        IShellDialogCommandService dialogCommandService,
        IShellNavigationCommandService navigationCommandService,
        IShellTabCommandService tabCommandService,
        IShellStatusService statusService,
        Action toggleCommandBarMultiLine,
        PluginHostBridge? pluginHostBridge = null)
    {
        ArgumentNullException.ThrowIfNull(promptComposerViewModel);
        ArgumentNullException.ThrowIfNull(threadWorkspaceViewModel);
        ArgumentNullException.ThrowIfNull(threadCommandCoordinator);
        ArgumentNullException.ThrowIfNull(promptInputService);
        ArgumentNullException.ThrowIfNull(threadCommandService);
        ArgumentNullException.ThrowIfNull(dialogCommandService);
        ArgumentNullException.ThrowIfNull(navigationCommandService);
        ArgumentNullException.ThrowIfNull(tabCommandService);
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(toggleCommandBarMultiLine);

        _promptComposerViewModel = promptComposerViewModel;
        _threadWorkspaceViewModel = threadWorkspaceViewModel;
        _threadCommandCoordinator = threadCommandCoordinator;
        _promptInputService = promptInputService;
        _threadCommandService = threadCommandService;
        _dialogCommandService = dialogCommandService;
        _navigationCommandService = navigationCommandService;
        _tabCommandService = tabCommandService;
        _toggleCommandBarMultiLine = toggleCommandBarMultiLine;
        _statusService = statusService;
        _pluginHostBridge = pluginHostBridge;
        _shellCommandRegistry = new ShellCommandRegistryFactory(
            _threadCommandCoordinator,
            _threadCommandService,
            _dialogCommandService,
            _navigationCommandService,
            _tabCommandService,
            _statusService,
            _pluginHostBridge).Create(this);
        _shellCommandDispatcher = new ShellCommandDispatcher(_shellCommandRegistry);
        _shellInputCoordinator = new ShellInputCoordinator(
            new ShellInputRouter(),
            _promptInputService.GetPromptText,
            _promptInputService.IsCurrentPromptEmpty,
            _shellCommandDispatcher);
    }

    public IReadOnlyList<ThreadWorkspaceCommandBinding> BuildWorkspaceCommandBindings()
    {
        var bindings = new List<ThreadWorkspaceCommandBinding>
        {
            CreateRegisteredCommandBinding("CodeAlta.Shell.Help"),
            CreateRegisteredCommandBinding("CodeAlta.Project.OpenFolder"),
            CreateRegisteredCommandBinding("CodeAlta.Providers.Manage"),
            CreateRegisteredCommandBinding("CodeAlta.File.Edit"),
            CreateRegisteredCommandBinding("CodeAlta.Skills.Manage"),
            CreateRegisteredCommandBinding("CodeAlta.Plugins.Manage"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.SessionUsage"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.Info"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.ExpandPrompt"),
            CreateCommandBinding("CodeAlta.Thread.Steer", () => ObserveUiTask(() => _shellInputCoordinator.SubmitCurrentPromptAsync(steer: true), "steer the current thread")),
            CreateRegisteredCommandBinding("CodeAlta.Thread.Abort"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.ClearQueue"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.Compact"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.CloseTab"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.TabLeft"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.TabRight"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.MessagePrevious"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.MessageNext"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.MessageFirst"),
            CreateRegisteredCommandBinding("CodeAlta.Thread.MessageLast"),
        };
        AddPluginCommandBindings(bindings);
        return bindings;
    }

    public Task HandleAcceptedPromptAsync(string? rawInput, CancellationToken cancellationToken = default)
        => _shellInputCoordinator.HandleAcceptedPromptAsync(rawInput, cancellationToken);

    public Task SubmitCurrentPromptAsync(bool steer, CancellationToken cancellationToken = default)
        => _shellInputCoordinator.SubmitCurrentPromptAsync(steer, cancellationToken);

    public Task AbortSelectedThreadAsync(CancellationToken cancellationToken = default)
        => _shellInputCoordinator.AbortSelectedThreadAsync(cancellationToken);

    public Task CompactSelectedThreadAsync(CancellationToken cancellationToken = default)
        => _shellInputCoordinator.CompactSelectedThreadAsync(cancellationToken);

    public Task CloseCurrentTabAsync(CancellationToken cancellationToken = default)
        => _shellInputCoordinator.CloseCurrentTabAsync(cancellationToken);

    public Task ShowHelpAsync(string? filterText = null, CancellationToken cancellationToken = default)
        => DispatchShellCommandAsync(new OpenHelpCommand(filterText), cancellationToken);

    Task IShellCommandSurfacePresenter.ShowHelpDialogAsync(string? filterText)
        => ShowShellHelpAsync(filterText);

    public void ShowCommandPalette()
    {
        var app = _dialogCommandService.GetDialogFocusTarget()?.App ?? _commandPalette?.App;
        _activeCommandPaletteStyle = ResolveCommandPalettePopupStyle(app?.FocusedElement);
        (_commandPalette ??= CreateCommandPalette(() => _activeCommandPaletteStyle)).Show();
    }

    public Task ShowCommandPaletteAsync()
        => DispatchShellCommandAsync(new OpenCommandPaletteCommand());

    public Task ExitAppAsync()
        => DispatchShellCommandAsync(new ExitAppCommand());

    public Task FocusSidebarAsync()
        => DispatchShellCommandAsync(new FocusSidebarCommand());

    public Task FocusPromptAsync()
        => DispatchShellCommandAsync(new FocusPromptCommand());

    public void ToggleCommandBarMultiLine()
        => _toggleCommandBarMultiLine();

    public Task ShowOpenFolderDialogAsync(string? initialPath = null)
        => DispatchShellCommandAsync(new OpenFolderCommand(initialPath));

    public Task OpenModelProvidersAsync()
        => DispatchShellCommandAsync(new OpenModelProvidersCommand());

    public Task OpenFileEditorAsync()
        => DispatchShellCommandAsync(new OpenFileEditorCommand());

    public Task OpenSkillsAsync()
        => DispatchShellCommandAsync(new OpenSkillsCommand());

    public Task OpenPluginsAsync()
        => DispatchShellCommandAsync(new OpenPluginsCommand());

    public void ShowOpenFolderDialog(string? initialPath = null)
        => new DirectoryPathDialog(
            "Open Project",
            "Type a project name from the sidebar or a rooted folder path.",
            "Open",
            _dialogCommandService.OpenFolderAsync,
            _dialogCommandService.GetDialogBounds,
            _dialogCommandService.GetDialogFocusTarget,
            _dialogCommandService.GetDialogFocusTarget,
            _dialogCommandService.GetProjects,
            initialPath,
            placeholder: "CodeAlta or C:\\code\\SomeFolder")
            .Show();

    private ThreadWorkspaceCommandBinding CreateCommandBinding(string commandId, Action execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(execute);

        var metadata = ShellCommandCatalog.Get(commandId);
        return new ThreadWorkspaceCommandBinding(
            metadata,
            execute,
            () => CanExecuteShellCommand(metadata.Availability));
    }

    private ThreadWorkspaceCommandBinding CreateCommandBinding(string commandId, ShellCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(command);

        return CreateCommandBinding(
            commandId,
            () => ObserveUiTask(() => DispatchShellCommandAsync(command), $"run command {commandId}"));
    }

    private ThreadWorkspaceCommandBinding CreateRegisteredCommandBinding(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        if (!_shellCommandRegistry.TryCreateCommand(commandId, out var command))
        {
            throw new InvalidOperationException($"No shell command factory is registered for {commandId}.");
        }

        return CreateCommandBinding(commandId, command);
    }

    private async Task DispatchShellCommandAsync(ShellCommand command, CancellationToken cancellationToken = default)
        => await _shellCommandDispatcher.DispatchAsync(command, cancellationToken);

    private void AddPluginCommandBindings(List<ThreadWorkspaceCommandBinding> bindings)
    {
        if (_pluginHostBridge is null)
        {
            return;
        }

        foreach (var contribution in _pluginHostBridge.GetCommandContributions())
        {
            var metadata = CreatePluginCommandMetadata(contribution);
            bindings.Add(new ThreadWorkspaceCommandBinding(
                metadata,
                () => ObserveUiTask(() => ExecutePluginCommandAsync(contribution.Name, null, CancellationToken.None), $"run plugin command {contribution.Name}"),
                () => CanExecutePluginCommand(contribution.Availability)));
        }
    }

    private static ShellCommandMetadata CreatePluginCommandMetadata(PluginCommandContribution contribution)
    {
        var presentation = contribution.Presentation;
        var keyBinding = contribution.KeyBinding;
        return new ShellCommandMetadata(
            $"Plugin.{contribution.Name}",
            contribution.Label ?? contribution.Name,
            contribution.Description ?? $"Run plugin command '{contribution.Name}'.",
            ShellCommandHelpCategory.General,
            contribution.Kind == PluginCommandKind.Thread ? ShellCommandScope.ThreadOnly : ShellCommandScope.AnyShell,
            ShellCommandAvailability.Always,
            keyBinding?.Gesture,
            keyBinding?.Sequence,
            contribution.Name,
            contribution.Aliases,
            presentation.ShowInCommandBar,
            presentation.ShowInCommandPalette,
            SupportsTextCommand: true,
            presentation.ShowInHelp);
    }

    private bool CanExecutePluginCommand(PluginCommandAvailability availability)
    {
        if (availability.RequiresProject && _threadCommandService.GetSelectedThread()?.ProjectRef is null)
        {
            return false;
        }

        if (availability.RequiresThread && _threadCommandService.GetSelectedThread() is null)
        {
            return false;
        }

        if (availability.RequiresIdleThread && (_threadCommandService.GetSelectedThread() is not { } idleThread || _threadCommandService.EnsureThreadTab(idleThread).StatusBusy))
        {
            return false;
        }

        if (availability.RequiresBusyThread && (_threadCommandService.GetSelectedThread() is not { } busyThread || !_threadCommandService.EnsureThreadTab(busyThread).StatusBusy))
        {
            return false;
        }

        var backendThread = _threadCommandService.GetSelectedThread();
        if ((availability.RequiresCodeAltaManagedBackend || availability.BackendFamilies.Count > 0) && backendThread is null)
        {
            return false;
        }

        if (availability.RequiresCodeAltaManagedBackend && !IsCodeAltaManagedBackend(backendThread!.BackendId))
        {
            return false;
        }

        if (availability.BackendFamilies.Count > 0 && !availability.BackendFamilies.Contains(backendThread!.BackendId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsCodeAltaManagedBackend(string backendId)
        => !string.Equals(backendId, AgentBackendIds.Codex.Value, StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(backendId, AgentBackendIds.Copilot.Value, StringComparison.OrdinalIgnoreCase);

    private async Task ExecutePluginCommandAsync(string name, string? arguments, CancellationToken cancellationToken)
    {
        if (_pluginHostBridge is null)
        {
            return;
        }

        var result = await _pluginHostBridge.ExecuteCommandAsync(name, arguments, cancellationToken);
        if (!string.IsNullOrWhiteSpace(result.UserMessage))
        {
            _statusService.SetStatus(result.UserMessage);
        }

        if (!string.IsNullOrWhiteSpace(result.PromptText))
        {
            await _threadCommandCoordinator.SendPromptAsync(result.PromptText, steer: false, cancellationToken);
        }
    }

    internal static string BuildUnknownCommandStatus(string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return $"Unknown command '/{commandName}'. Press F1 or type /help.";
    }

    private bool CanExecuteShellCommand(ShellCommandAvailability availability)
    {
        return availability switch
        {
            ShellCommandAvailability.Always => true,
            ShellCommandAvailability.PromptEnabled => _promptComposerViewModel.IsEnabled,
            ShellCommandAvailability.CanSend => _promptComposerViewModel.CanSend,
            ShellCommandAvailability.CanSteer => _promptComposerViewModel.CanSteer,
            ShellCommandAvailability.CanAbort => _promptComposerViewModel.CanAbort,
            ShellCommandAvailability.CanClearQueue => _promptComposerViewModel.CanClearQueue,
            ShellCommandAvailability.CanCompact => _promptComposerViewModel.CanCompact,
            ShellCommandAvailability.CanCloseTab => _promptComposerViewModel.CanCloseTab,
            ShellCommandAvailability.CanShowThreadInfo => _threadWorkspaceViewModel.CanShowThreadInfo,
            _ => false,
        };
    }

    private void ObserveUiTask(Func<Task> taskFactory, string operation)
        => _ = UiTaskDiagnostics.ObserveAsync(taskFactory, operation, _statusService.SetStatus);

    private Task ShowShellHelpAsync(string? filterText = null)
    {
        _helpDialog ??= new ShellHelpDialog(_dialogCommandService.GetDialogBounds, _dialogCommandService.GetDialogFocusTarget);
        return _helpDialog.ShowAsync(filterText);
    }

    internal static CommandPaletteStyle ResolveCommandPalettePopupStyle(Visual? focusElement)
    {
        return IsInsideDialog(focusElement)
            ? DialogCommandPalettePopupStyle
            : CommandPalettePopupStyle;
    }

    private static bool IsInsideDialog(Visual? visual)
    {
        for (var current = visual; current is not null; current = current.Parent)
        {
            if (current is Dialog)
            {
                return true;
            }
        }

        return false;
    }

    private static CommandPalette CreateCommandPalette(Func<CommandPaletteStyle> getStyle)
        => new CommandPalette().Style(() => getStyle());
}
