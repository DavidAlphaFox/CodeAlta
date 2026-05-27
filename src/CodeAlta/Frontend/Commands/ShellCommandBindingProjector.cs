using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.Plugins.Abstractions;
using CodeAlta.ViewModels;

namespace CodeAlta.Frontend.Commands;

internal sealed class ShellCommandBindingProjector
{
    private readonly PromptComposerViewModel _promptComposerViewModel;
    private readonly SessionWorkspaceViewModel _sessionWorkspaceViewModel;
    private readonly IShellSessionCommandService _sessionCommandService;
    private readonly IShellStatusService _statusService;
    private readonly ShellCommandRegistry _shellCommandRegistry;
    private readonly IShellCommandDispatcher _shellCommandDispatcher;
    private readonly IPluginCommandService _pluginCommandService;

    public ShellCommandBindingProjector(
        PromptComposerViewModel promptComposerViewModel,
        SessionWorkspaceViewModel sessionWorkspaceViewModel,
        IShellSessionCommandService sessionCommandService,
        IShellStatusService statusService,
        ShellCommandRegistry shellCommandRegistry,
        IShellCommandDispatcher shellCommandDispatcher,
        IPluginCommandService pluginCommandService)
    {
        ArgumentNullException.ThrowIfNull(promptComposerViewModel);
        ArgumentNullException.ThrowIfNull(sessionWorkspaceViewModel);
        ArgumentNullException.ThrowIfNull(sessionCommandService);
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(shellCommandRegistry);
        ArgumentNullException.ThrowIfNull(shellCommandDispatcher);
        ArgumentNullException.ThrowIfNull(pluginCommandService);

        _promptComposerViewModel = promptComposerViewModel;
        _sessionWorkspaceViewModel = sessionWorkspaceViewModel;
        _sessionCommandService = sessionCommandService;
        _statusService = statusService;
        _shellCommandRegistry = shellCommandRegistry;
        _shellCommandDispatcher = shellCommandDispatcher;
        _pluginCommandService = pluginCommandService;
    }

    public IReadOnlyList<SessionWorkspaceCommandBinding> BuildWorkspaceCommandBindings()
    {
        var bindings = ShellCommandCatalog.Commands
            .Where(static metadata => metadata.Scope != ShellCommandScope.AnyShell || metadata.Id == "CodeAlta.Shell.Help")
            .Select(command => CreateRegisteredCommandBinding(command.Id))
            .ToList();

        AddPluginCommandBindings(bindings);
        return bindings;
    }

    private SessionWorkspaceCommandBinding CreateCommandBinding(string commandId, ShellCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(command);

        var metadata = ShellCommandCatalog.Get(commandId);
        return new SessionWorkspaceCommandBinding(
            metadata,
            () => ObserveUiTask(() => DispatchShellCommandAsync(command), $"run command {commandId}"),
            () => CanExecuteShellCommand(metadata.Availability));
    }

    private SessionWorkspaceCommandBinding CreateRegisteredCommandBinding(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        if (!_shellCommandRegistry.TryCreateCommand(commandId, out var command))
        {
            throw new InvalidOperationException($"No shell command factory is registered for {commandId}.");
        }

        return CreateCommandBinding(commandId, command);
    }

    private void AddPluginCommandBindings(List<SessionWorkspaceCommandBinding> bindings)
    {
        foreach (var contribution in _pluginCommandService.GetCommandContributions())
        {
            var metadata = CreatePluginCommandMetadata(contribution);
            bindings.Add(new SessionWorkspaceCommandBinding(
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
            contribution.Kind == PluginCommandKind.Session ? ShellCommandScope.SessionOnly : ShellCommandScope.AnyShell,
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
        if (availability.RequiresProject && _sessionCommandService.GetSelectedSession()?.ProjectRef is null)
        {
            return false;
        }

        if (availability.RequiresSession && _sessionCommandService.GetSelectedSession() is null)
        {
            return false;
        }

        if (availability.RequiresIdleSession && (_sessionCommandService.GetSelectedSession() is not { } idleSession || _sessionCommandService.EnsureSessionTab(idleSession).StatusBusy))
        {
            return false;
        }

        if (availability.RequiresBusySession && (_sessionCommandService.GetSelectedSession() is not { } busySession || !_sessionCommandService.EnsureSessionTab(busySession).StatusBusy))
        {
            return false;
        }

        var backendSession = _sessionCommandService.GetSelectedSession();
        if ((availability.RequiresCodeAltaManagedBackend || availability.BackendFamilies.Count > 0) && backendSession is null)
        {
            return false;
        }

        if (availability.RequiresCodeAltaManagedBackend && !IsCodeAltaManagedBackend(backendSession!.ProviderId))
        {
            return false;
        }

        if (availability.BackendFamilies.Count > 0 && !availability.BackendFamilies.Contains(backendSession!.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsCodeAltaManagedBackend(string ProviderId)
        => !string.Equals(ProviderId, ModelProviderIds.Codex.Value, StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(ProviderId, ModelProviderIds.Copilot.Value, StringComparison.OrdinalIgnoreCase);

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
            ShellCommandAvailability.CanShowSessionInfo => _sessionWorkspaceViewModel.CanShowSessionInfo,
            _ => false,
        };
    }

    private async Task DispatchShellCommandAsync(ShellCommand command, CancellationToken cancellationToken = default)
        => await _shellCommandDispatcher.DispatchAsync(command, cancellationToken);

    private void ObserveUiTask(Func<Task> taskFactory, string operation)
        => _ = UiTaskDiagnostics.ObserveAsync(taskFactory, operation, _statusService.SetStatus);
}
