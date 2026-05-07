using CodeAlta.App;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Plugins.Abstractions;
using CodeAlta.Views;

namespace CodeAlta.Frontend.Commands;

internal static class PromptCommandHandlers
{
    public static void Register(ShellCommandRegistry registry, ThreadCommandCoordinator threadCommands)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(threadCommands);

        registry.Register<SubmitPromptCommand>((command, cancellationToken) => ToValueTask(threadCommands.SendPromptAsync(command.Text, command.Steer, cancellationToken)));
    }

    private static ValueTask ToValueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new ValueTask(task);
    }
}

internal static class ThreadCommandHandlers
{
    public static void Register(
        ShellCommandRegistry registry,
        ThreadCommandCoordinator threadCommands,
        Func<WorkThreadDescriptor?> getSelectedThread,
        Func<WorkThreadDescriptor, OpenThreadState> ensureThreadTab,
        Action<string, bool, StatusTone> setStatus)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(threadCommands);
        ArgumentNullException.ThrowIfNull(getSelectedThread);
        ArgumentNullException.ThrowIfNull(ensureThreadTab);
        ArgumentNullException.ThrowIfNull(setStatus);

        registry.Register<AbortSelectedThreadCommand>((_, _) => ToValueTask(threadCommands.AbortSelectedThreadAsync()));
        registry.Register<CompactSelectedThreadCommand>((_, _) => ToValueTask(threadCommands.CompactSelectedThreadAsync()));
        registry.Register<ShowQueueStatusCommand>((_, _) => ToValueTask(ShowSelectedThreadQueueStatusAsync(getSelectedThread, ensureThreadTab, setStatus)));
        registry.Register<ClearSelectedThreadQueueCommand>((_, _) => ToValueTask(threadCommands.ClearSelectedThreadQueueAsync()));
    }

    private static Task ShowSelectedThreadQueueStatusAsync(
        Func<WorkThreadDescriptor?> getSelectedThread,
        Func<WorkThreadDescriptor, OpenThreadState> ensureThreadTab,
        Action<string, bool, StatusTone> setStatus)
    {
        if (getSelectedThread() is not { } thread)
        {
            setStatus("Open a thread before inspecting its queue.", false, StatusTone.Warning);
            return Task.CompletedTask;
        }

        var tab = ensureThreadTab(thread);
        var queuedCount = tab.QueuedPrompts.Count;
        var tone = queuedCount == 0
            ? StatusTone.Ready
            : tab.StatusBusy ? StatusTone.Info : StatusTone.Warning;
        var message = queuedCount == 0
            ? $"Queue empty · {thread.Title}"
            : $"{queuedCount} queued prompt(s) waiting in '{thread.Title}'.";

        setStatus(message, false, tone);
        return Task.CompletedTask;
    }

    private static ValueTask ToValueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new ValueTask(task);
    }
}

internal static class NavigationCommandHandlers
{
    public static void Register(
        ShellCommandRegistry registry,
        Action focusSidebar,
        Action focusPrompt,
        Func<Task> selectTabLeftAsync,
        Func<Task> selectTabRightAsync,
        Func<Task> scrollToPreviousMessageAsync,
        Func<Task> scrollToNextMessageAsync,
        Func<Task> scrollToFirstMessageAsync,
        Func<Task> scrollToLastMessageAsync)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(focusSidebar);
        ArgumentNullException.ThrowIfNull(focusPrompt);
        ArgumentNullException.ThrowIfNull(selectTabLeftAsync);
        ArgumentNullException.ThrowIfNull(selectTabRightAsync);
        ArgumentNullException.ThrowIfNull(scrollToPreviousMessageAsync);
        ArgumentNullException.ThrowIfNull(scrollToNextMessageAsync);
        ArgumentNullException.ThrowIfNull(scrollToFirstMessageAsync);
        ArgumentNullException.ThrowIfNull(scrollToLastMessageAsync);

        registry.Register<FocusSidebarCommand>((_, _) =>
        {
            focusSidebar();
            return ValueTask.CompletedTask;
        });
        registry.Register<FocusPromptCommand>((_, _) =>
        {
            focusPrompt();
            return ValueTask.CompletedTask;
        });
        registry.Register<SelectRelativeTabCommand>((command, _) => ToValueTask(command.Offset < 0 ? selectTabLeftAsync() : selectTabRightAsync()));
        registry.Register<ScrollSelectedThreadMessageCommand>((command, _) => ToValueTask(ScrollToMessageAsync(
            command.Target,
            scrollToPreviousMessageAsync,
            scrollToNextMessageAsync,
            scrollToFirstMessageAsync,
            scrollToLastMessageAsync)));
    }

    private static Task ScrollToMessageAsync(
        ThreadMessageScrollTarget target,
        Func<Task> scrollToPreviousMessageAsync,
        Func<Task> scrollToNextMessageAsync,
        Func<Task> scrollToFirstMessageAsync,
        Func<Task> scrollToLastMessageAsync)
    {
        return target switch
        {
            ThreadMessageScrollTarget.Previous => scrollToPreviousMessageAsync(),
            ThreadMessageScrollTarget.Next => scrollToNextMessageAsync(),
            ThreadMessageScrollTarget.First => scrollToFirstMessageAsync(),
            ThreadMessageScrollTarget.Last => scrollToLastMessageAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown thread message scroll target."),
        };
    }

    private static ValueTask ToValueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new ValueTask(task);
    }
}

internal static class DialogCommandHandlers
{
    public static void Register(
        ShellCommandRegistry registry,
        Func<string?, Task> showHelpAsync,
        Action showCommandPalette,
        Action exitApp,
        Action<string?> showOpenFolderDialog,
        Func<Task> openModelProvidersAsync,
        Func<Task> openFileEditorAsync,
        Func<Task> openSkillsAsync,
        Func<Task> openPluginsAsync,
        Action openSessionUsage,
        Action openThreadInfo,
        Action openExpandedPromptEditor)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(showHelpAsync);
        ArgumentNullException.ThrowIfNull(showCommandPalette);
        ArgumentNullException.ThrowIfNull(exitApp);
        ArgumentNullException.ThrowIfNull(showOpenFolderDialog);
        ArgumentNullException.ThrowIfNull(openModelProvidersAsync);
        ArgumentNullException.ThrowIfNull(openFileEditorAsync);
        ArgumentNullException.ThrowIfNull(openSkillsAsync);
        ArgumentNullException.ThrowIfNull(openPluginsAsync);
        ArgumentNullException.ThrowIfNull(openSessionUsage);
        ArgumentNullException.ThrowIfNull(openThreadInfo);
        ArgumentNullException.ThrowIfNull(openExpandedPromptEditor);

        registry.Register<OpenHelpCommand>((command, _) => ToValueTask(showHelpAsync(command.FilterText)));
        registry.Register<OpenCommandPaletteCommand>((_, _) =>
        {
            showCommandPalette();
            return ValueTask.CompletedTask;
        });
        registry.Register<ExitAppCommand>((_, _) =>
        {
            exitApp();
            return ValueTask.CompletedTask;
        });
        registry.Register<OpenFolderCommand>((command, _) =>
        {
            showOpenFolderDialog(command.InitialPath);
            return ValueTask.CompletedTask;
        });
        registry.Register<OpenModelProvidersCommand>((_, _) => ToValueTask(openModelProvidersAsync()));
        registry.Register<OpenFileEditorCommand>((_, _) => ToValueTask(openFileEditorAsync()));
        registry.Register<OpenSkillsCommand>((_, _) => ToValueTask(openSkillsAsync()));
        registry.Register<OpenPluginsCommand>((_, _) => ToValueTask(openPluginsAsync()));
        registry.Register<OpenSessionUsageCommand>((_, _) =>
        {
            openSessionUsage();
            return ValueTask.CompletedTask;
        });
        registry.Register<OpenThreadInfoCommand>((_, _) =>
        {
            openThreadInfo();
            return ValueTask.CompletedTask;
        });
        registry.Register<OpenExpandedPromptCommand>((_, _) =>
        {
            openExpandedPromptEditor();
            return ValueTask.CompletedTask;
        });
    }

    private static ValueTask ToValueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new ValueTask(task);
    }
}

internal static class TabCommandHandlers
{
    public static void Register(ShellCommandRegistry registry, Func<Task> closeCurrentTabAsync)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(closeCurrentTabAsync);

        registry.Register<CloseCurrentTabCommand>((_, _) => ToValueTask(closeCurrentTabAsync()));
    }

    private static ValueTask ToValueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new ValueTask(task);
    }
}

internal static class PluginCommandHandlers
{
    public static void Register(
        ShellCommandRegistry registry,
        PluginHostBridge? pluginHostBridge,
        ThreadCommandCoordinator threadCommands,
        IShellStatusService statusService,
        Action<string, bool, StatusTone> setStatus)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(threadCommands);
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(setStatus);

        registry.Register<ExecutePluginTextCommand>((command, cancellationToken) => ToValueTask(ExecutePluginTextCommandAsync(
            pluginHostBridge,
            threadCommands,
            statusService,
            setStatus,
            command.CommandName,
            command.Arguments,
            cancellationToken)));
    }

    private static async Task ExecutePluginTextCommandAsync(
        PluginHostBridge? pluginHostBridge,
        ThreadCommandCoordinator threadCommands,
        IShellStatusService statusService,
        Action<string, bool, StatusTone> setStatus,
        string name,
        string? arguments,
        CancellationToken cancellationToken)
    {
        if (pluginHostBridge is not null)
        {
            var result = await pluginHostBridge.ExecuteCommandAsync(name, arguments, cancellationToken);
            if (result.Disposition != PluginCommandDisposition.NotHandled)
            {
                if (!string.IsNullOrWhiteSpace(result.UserMessage))
                {
                    setStatus(result.UserMessage, false, StatusTone.Info);
                }

                if (!string.IsNullOrWhiteSpace(result.PromptText))
                {
                    await threadCommands.SendPromptAsync(result.PromptText, steer: false, cancellationToken);
                }

                return;
            }
        }

        statusService.SetStatus(ShellCommandSurfaceCoordinator.BuildUnknownCommandStatus(name), tone: StatusTone.Warning);
    }

    private static ValueTask ToValueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new ValueTask(task);
    }
}
