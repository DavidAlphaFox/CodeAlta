using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.Plugins.Abstractions;
using CodeAlta.ViewModels;

namespace CodeAlta.Frontend.Commands;

internal sealed class ShellCommandBindingProjector
{
    private readonly PromptComposerViewModel _promptComposerViewModel;
    private readonly ThreadWorkspaceViewModel _threadWorkspaceViewModel;
    private readonly IShellThreadCommandService _threadCommandService;
    private readonly IShellStatusService _statusService;
    private readonly ShellCommandRegistry _shellCommandRegistry;
    private readonly IShellCommandDispatcher _shellCommandDispatcher;
    private readonly IPluginCommandService _pluginCommandService;

    public ShellCommandBindingProjector(
        PromptComposerViewModel promptComposerViewModel,
        ThreadWorkspaceViewModel threadWorkspaceViewModel,
        IShellThreadCommandService threadCommandService,
        IShellStatusService statusService,
        ShellCommandRegistry shellCommandRegistry,
        IShellCommandDispatcher shellCommandDispatcher,
        IPluginCommandService pluginCommandService)
    {
        ArgumentNullException.ThrowIfNull(promptComposerViewModel);
        ArgumentNullException.ThrowIfNull(threadWorkspaceViewModel);
        ArgumentNullException.ThrowIfNull(threadCommandService);
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(shellCommandRegistry);
        ArgumentNullException.ThrowIfNull(shellCommandDispatcher);
        ArgumentNullException.ThrowIfNull(pluginCommandService);

        _promptComposerViewModel = promptComposerViewModel;
        _threadWorkspaceViewModel = threadWorkspaceViewModel;
        _threadCommandService = threadCommandService;
        _statusService = statusService;
        _shellCommandRegistry = shellCommandRegistry;
        _shellCommandDispatcher = shellCommandDispatcher;
        _pluginCommandService = pluginCommandService;
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
            CreateRegisteredCommandBinding("CodeAlta.Thread.Steer"),
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

    private ThreadWorkspaceCommandBinding CreateCommandBinding(string commandId, ShellCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(command);

        var metadata = ShellCommandCatalog.Get(commandId);
        return new ThreadWorkspaceCommandBinding(
            metadata,
            () => ObserveUiTask(() => DispatchShellCommandAsync(command), $"run command {commandId}"),
            () => CanExecuteShellCommand(metadata.Availability));
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

    private void AddPluginCommandBindings(List<ThreadWorkspaceCommandBinding> bindings)
    {
        foreach (var contribution in _pluginCommandService.GetCommandContributions())
        {
            var metadata = CreatePluginCommandMetadata(contribution);
            bindings.Add(new ThreadWorkspaceCommandBinding(
                metadata,
                () => ObserveUiTask(() => DispatchShellCommandAsync(new ExecutePluginTextCommand(contribution.Name, null)), $"run plugin command {contribution.Name}"),
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
            ShowInHelp: presentation.ShowInHelp);
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

    private async Task DispatchShellCommandAsync(ShellCommand command, CancellationToken cancellationToken = default)
        => await _shellCommandDispatcher.DispatchAsync(command, cancellationToken);

    private void ObserveUiTask(Func<Task> taskFactory, string operation)
        => _ = UiTaskDiagnostics.ObserveAsync(taskFactory, operation, _statusService.SetStatus);
}
