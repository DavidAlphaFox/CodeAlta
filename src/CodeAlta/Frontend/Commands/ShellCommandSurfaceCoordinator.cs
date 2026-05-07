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

internal sealed class ShellCommandSurfaceCoordinator
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
    private readonly Func<Rectangle?> _getHelpBounds;
    private readonly Func<Visual?> _getHelpFocusTarget;
    private readonly Func<IReadOnlyList<ProjectDescriptor>> _getProjects;
    private readonly Func<string, bool, Task> _openFolderAsync;
    private readonly Func<Task> _openModelProvidersAsync;
    private readonly Func<Task> _openFileEditorAsync;
    private readonly Func<Task> _openSkillsAsync;
    private readonly Func<Task> _openPluginsAsync;
    private readonly Func<Task> _closeCurrentTabAsync;
    private readonly Func<WorkThreadDescriptor?> _getSelectedThread;
    private readonly Func<WorkThreadDescriptor, OpenThreadState> _ensureThreadTab;
    private readonly Action _focusSidebar;
    private readonly Action _focusPrompt;
    private readonly Action _toggleCommandBarMultiLine;
    private readonly Action<string, bool, StatusTone> _setStatus;
    private readonly IShellStatusService _statusService;
    private readonly Action _openSessionUsage;
    private readonly Action _openThreadInfo;
    private readonly Action _openExpandedPromptEditor;
    private readonly Func<Task> _selectTabLeftAsync;
    private readonly Func<Task> _selectTabRightAsync;
    private readonly Func<Task> _scrollToPreviousMessageAsync;
    private readonly Func<Task> _scrollToNextMessageAsync;
    private readonly Func<Task> _scrollToFirstMessageAsync;
    private readonly Func<Task> _scrollToLastMessageAsync;
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
        Func<IReadOnlyList<ProjectDescriptor>> getProjects,
        Func<string, bool, Task> openFolderAsync,
        Func<Task> openModelProvidersAsync,
        Func<Task> openFileEditorAsync,
        Func<Task> openSkillsAsync,
        Func<Task> openPluginsAsync,
        Func<string?> getPromptText,
        Func<Task> closeCurrentTabAsync,
        Action<string, bool, StatusTone> setStatus,
        Func<Rectangle?> getHelpBounds,
        Func<Visual?> getHelpFocusTarget,
        Func<WorkThreadDescriptor?> getSelectedThread,
        Func<WorkThreadDescriptor, OpenThreadState> ensureThreadTab,
        Action focusSidebar,
        Action focusPrompt,
        Action toggleCommandBarMultiLine,
        Action openSessionUsage,
        Action openThreadInfo,
        Action openExpandedPromptEditor,
        Func<Task> selectTabLeftAsync,
        Func<Task> selectTabRightAsync,
        Func<Task> scrollToPreviousMessageAsync,
        Func<Task> scrollToNextMessageAsync,
        Func<Task> scrollToFirstMessageAsync,
        Func<Task> scrollToLastMessageAsync,
        PluginHostBridge? pluginHostBridge = null)
    {
        ArgumentNullException.ThrowIfNull(promptComposerViewModel);
        ArgumentNullException.ThrowIfNull(threadWorkspaceViewModel);
        ArgumentNullException.ThrowIfNull(threadCommandCoordinator);
        ArgumentNullException.ThrowIfNull(getProjects);
        ArgumentNullException.ThrowIfNull(openFolderAsync);
        ArgumentNullException.ThrowIfNull(openModelProvidersAsync);
        ArgumentNullException.ThrowIfNull(openFileEditorAsync);
        ArgumentNullException.ThrowIfNull(openSkillsAsync);
        ArgumentNullException.ThrowIfNull(openPluginsAsync);
        ArgumentNullException.ThrowIfNull(getPromptText);
        ArgumentNullException.ThrowIfNull(closeCurrentTabAsync);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(getHelpBounds);
        ArgumentNullException.ThrowIfNull(getHelpFocusTarget);
        ArgumentNullException.ThrowIfNull(getSelectedThread);
        ArgumentNullException.ThrowIfNull(ensureThreadTab);
        ArgumentNullException.ThrowIfNull(focusSidebar);
        ArgumentNullException.ThrowIfNull(focusPrompt);
        ArgumentNullException.ThrowIfNull(toggleCommandBarMultiLine);
        ArgumentNullException.ThrowIfNull(openSessionUsage);
        ArgumentNullException.ThrowIfNull(openThreadInfo);
        ArgumentNullException.ThrowIfNull(openExpandedPromptEditor);
        ArgumentNullException.ThrowIfNull(selectTabLeftAsync);
        ArgumentNullException.ThrowIfNull(selectTabRightAsync);
        ArgumentNullException.ThrowIfNull(scrollToPreviousMessageAsync);
        ArgumentNullException.ThrowIfNull(scrollToNextMessageAsync);
        ArgumentNullException.ThrowIfNull(scrollToFirstMessageAsync);
        ArgumentNullException.ThrowIfNull(scrollToLastMessageAsync);

        _promptComposerViewModel = promptComposerViewModel;
        _threadWorkspaceViewModel = threadWorkspaceViewModel;
        _threadCommandCoordinator = threadCommandCoordinator;
        _getProjects = getProjects;
        _openFolderAsync = openFolderAsync;
        _openModelProvidersAsync = openModelProvidersAsync;
        _openFileEditorAsync = openFileEditorAsync;
        _openSkillsAsync = openSkillsAsync;
        _openPluginsAsync = openPluginsAsync;
        _closeCurrentTabAsync = closeCurrentTabAsync;
        _getHelpBounds = getHelpBounds;
        _getHelpFocusTarget = getHelpFocusTarget;
        _getSelectedThread = getSelectedThread;
        _ensureThreadTab = ensureThreadTab;
        _focusSidebar = focusSidebar;
        _focusPrompt = focusPrompt;
        _toggleCommandBarMultiLine = toggleCommandBarMultiLine;
        _setStatus = setStatus;
        _statusService = new DelegatingShellStatusService(setStatus);
        _openSessionUsage = openSessionUsage;
        _openThreadInfo = openThreadInfo;
        _openExpandedPromptEditor = openExpandedPromptEditor;
        _selectTabLeftAsync = selectTabLeftAsync;
        _selectTabRightAsync = selectTabRightAsync;
        _scrollToPreviousMessageAsync = scrollToPreviousMessageAsync;
        _scrollToNextMessageAsync = scrollToNextMessageAsync;
        _scrollToFirstMessageAsync = scrollToFirstMessageAsync;
        _scrollToLastMessageAsync = scrollToLastMessageAsync;
        _pluginHostBridge = pluginHostBridge;
        _shellCommandRegistry = CreateCommandRegistry();
        _shellCommandDispatcher = new ShellCommandDispatcher(_shellCommandRegistry);
        _shellInputCoordinator = new ShellInputCoordinator(
            new ShellInputRouter(),
            getPromptText,
            threadCommandCoordinator.IsCurrentPromptEmpty,
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

    public void ShowCommandPalette()
    {
        var app = _getHelpFocusTarget()?.App ?? _commandPalette?.App;
        _activeCommandPaletteStyle = ResolveCommandPalettePopupStyle(app?.FocusedElement);
        (_commandPalette ??= CreateCommandPalette(() => _activeCommandPaletteStyle)).Show();
    }

    public Task ShowCommandPaletteAsync()
        => DispatchShellCommandAsync(new OpenCommandPaletteCommand());

    public Task ExitAppAsync()
        => DispatchShellCommandAsync(new ExitAppCommand());

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

    private void ShowOpenFolderDialogCore(string? initialPath)
        => new DirectoryPathDialog(
            "Open Project",
            "Type a project name from the sidebar or a rooted folder path.",
            "Open",
            _openFolderAsync,
            _getHelpBounds,
            _getHelpFocusTarget,
            _getHelpFocusTarget,
            () => _getProjects(),
            initialPath,
            placeholder: "CodeAlta or C:\\code\\SomeFolder")
            .Show();

    private ShellCommandRegistry CreateCommandRegistry()
    {
        var registry = new ShellCommandRegistry();
        registry.RegisterFactory("CodeAlta.Shell.Help", static () => new OpenHelpCommand());
        registry.RegisterFactory("CodeAlta.Project.OpenFolder", static () => new OpenFolderCommand());
        registry.RegisterFactory("CodeAlta.Providers.Manage", static () => new OpenModelProvidersCommand());
        registry.RegisterFactory("CodeAlta.File.Edit", static () => new OpenFileEditorCommand());
        registry.RegisterFactory("CodeAlta.Skills.Manage", static () => new OpenSkillsCommand());
        registry.RegisterFactory("CodeAlta.Plugins.Manage", static () => new OpenPluginsCommand());
        registry.RegisterFactory("CodeAlta.Thread.SessionUsage", static () => new OpenSessionUsageCommand());
        registry.RegisterFactory("CodeAlta.Thread.Info", static () => new OpenThreadInfoCommand());
        registry.RegisterFactory("CodeAlta.Thread.ExpandPrompt", static () => new OpenExpandedPromptCommand());
        registry.RegisterFactory("CodeAlta.Thread.Abort", static () => new AbortSelectedThreadCommand());
        registry.RegisterFactory("CodeAlta.Thread.ClearQueue", static () => new ClearSelectedThreadQueueCommand());
        registry.RegisterFactory("CodeAlta.Thread.Compact", static () => new CompactSelectedThreadCommand());
        registry.RegisterFactory("CodeAlta.Thread.CloseTab", static () => new CloseCurrentTabCommand());
        registry.RegisterFactory("CodeAlta.Thread.TabLeft", static () => new SelectRelativeTabCommand(-1));
        registry.RegisterFactory("CodeAlta.Thread.TabRight", static () => new SelectRelativeTabCommand(1));
        registry.RegisterFactory("CodeAlta.Thread.MessagePrevious", static () => new ScrollSelectedThreadMessageCommand(ThreadMessageScrollTarget.Previous));
        registry.RegisterFactory("CodeAlta.Thread.MessageNext", static () => new ScrollSelectedThreadMessageCommand(ThreadMessageScrollTarget.Next));
        registry.RegisterFactory("CodeAlta.Thread.MessageFirst", static () => new ScrollSelectedThreadMessageCommand(ThreadMessageScrollTarget.First));
        registry.RegisterFactory("CodeAlta.Thread.MessageLast", static () => new ScrollSelectedThreadMessageCommand(ThreadMessageScrollTarget.Last));

        PromptCommandHandlers.Register(registry, _threadCommandCoordinator);
        ThreadCommandHandlers.Register(registry, _threadCommandCoordinator, _getSelectedThread, _ensureThreadTab, _setStatus);
        NavigationCommandHandlers.Register(
            registry,
            _focusSidebar,
            _focusPrompt,
            _selectTabLeftAsync,
            _selectTabRightAsync,
            _scrollToPreviousMessageAsync,
            _scrollToNextMessageAsync,
            _scrollToFirstMessageAsync,
            _scrollToLastMessageAsync);
        DialogCommandHandlers.Register(
            registry,
            ShowShellHelpAsync,
            ShowCommandPalette,
            () => _getHelpFocusTarget()?.App?.Stop(),
            ShowOpenFolderDialogCore,
            _openModelProvidersAsync,
            _openFileEditorAsync,
            _openSkillsAsync,
            _openPluginsAsync,
            _openSessionUsage,
            _openThreadInfo,
            _openExpandedPromptEditor);
        TabCommandHandlers.Register(registry, _closeCurrentTabAsync);
        PluginCommandHandlers.Register(registry, _pluginHostBridge, _threadCommandCoordinator, _statusService, _setStatus);
        return registry;
    }

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
        if (availability.RequiresProject && _getSelectedThread()?.ProjectRef is null)
        {
            return false;
        }

        if (availability.RequiresThread && _getSelectedThread() is null)
        {
            return false;
        }

        if (availability.RequiresIdleThread && (_getSelectedThread() is not { } idleThread || _ensureThreadTab(idleThread).StatusBusy))
        {
            return false;
        }

        if (availability.RequiresBusyThread && (_getSelectedThread() is not { } busyThread || !_ensureThreadTab(busyThread).StatusBusy))
        {
            return false;
        }

        var backendThread = _getSelectedThread();
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
            _setStatus(result.UserMessage, false, StatusTone.Info);
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
        => _ = UiTaskDiagnostics.ObserveAsync(taskFactory, operation, _setStatus);

    private Task ShowShellHelpAsync(string? filterText = null)
    {
        _helpDialog ??= new ShellHelpDialog(_getHelpBounds, _getHelpFocusTarget);
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
