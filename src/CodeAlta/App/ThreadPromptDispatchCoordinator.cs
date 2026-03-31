using CodeAlta.Agent;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Presentation.Shell;
using CodeAlta.Views;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal sealed class ThreadPromptDispatchCoordinator
{
    private const int MaxInitialThreadTitleLength = 80;
    private readonly WorkThreadRuntimeService _runtimeService;
    private readonly ThreadExecutionOptionsFactory _executionOptionsFactory;
    private readonly ThreadPromptQueueCoordinator _queueCoordinator;
    private readonly ThreadCommandContext _commandContext;

    public ThreadPromptDispatchCoordinator(
        WorkThreadRuntimeService runtimeService,
        ThreadExecutionOptionsFactory executionOptionsFactory,
        ThreadPromptQueueCoordinator queueCoordinator,
        ThreadCommandContext commandContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeService);
        ArgumentNullException.ThrowIfNull(executionOptionsFactory);
        ArgumentNullException.ThrowIfNull(queueCoordinator);
        ArgumentNullException.ThrowIfNull(commandContext);

        _runtimeService = runtimeService;
        _executionOptionsFactory = executionOptionsFactory;
        _queueCoordinator = queueCoordinator;
        _commandContext = commandContext;
    }

    public Task DispatchPromptAsync(
        WorkThreadDescriptor thread,
        OpenThreadState tab,
        string prompt,
        bool steer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        return DispatchPromptCoreAsync(thread, tab, prompt, steer, cancellationToken);
    }

    public WorkThreadExecutionOptions BuildExecutionOptions(WorkThreadDescriptor thread, OpenThreadState tab)
        => _executionOptionsFactory.BuildExecutionOptions(thread, tab);

    public WorkThreadExecutionOptions BuildPreferredExecutionOptions(
        AgentBackendId backendId,
        string workingDirectory,
        IReadOnlyList<string> projectRoots)
        => _executionOptionsFactory.BuildPreferredExecutionOptions(backendId, workingDirectory, projectRoots);

    public static string CreateInitialThreadTitle(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var normalized = prompt
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim();

        normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            return prompt.Trim();
        }

        var sentenceLength = FindFirstSentenceLength(normalized);
        var candidate = sentenceLength > 0
            ? normalized[..sentenceLength]
            : normalized;

        if (candidate.Length <= MaxInitialThreadTitleLength)
        {
            return candidate;
        }

        return candidate[..(MaxInitialThreadTitleLength - 3)].TrimEnd() + "...";
    }

    private static int FindFirstSentenceLength(string content)
    {
        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (ch is not ('.' or '!' or '?'))
            {
                continue;
            }

            if (i == content.Length - 1 || char.IsWhiteSpace(content[i + 1]))
            {
                return i + 1;
            }
        }

        return 0;
    }

    public WorkThreadExecutionOptions BuildDelegationExecutionOptions(
        string threadId,
        OpenThreadState tab,
        string workingDirectory,
        IReadOnlyList<string> projectRoots)
        => _executionOptionsFactory.BuildDelegationExecutionOptions(threadId, tab, workingDirectory, projectRoots);

    private async Task DispatchPromptCoreAsync(
        WorkThreadDescriptor thread,
        OpenThreadState tab,
        string prompt,
        bool steer,
        CancellationToken cancellationToken)
    {
        string? pendingSteerId = null;
        try
        {
            _commandContext.SetThreadStatus(tab, StatusVisualFormatter.BuildThinkingStatusText(), true, StatusTone.Info);
            var executionOptions = _executionOptionsFactory.BuildExecutionOptions(thread, tab);
            var dispatchAsSteer = steer && tab.ActiveRunId is not null;
            AgentRunId runId;
            if (dispatchAsSteer)
            {
                pendingSteerId = _queueCoordinator.AddPendingSteer(tab, prompt);
                runId = await _runtimeService.SteerAsync(
                        thread,
                        executionOptions,
                        new AgentSteerOptions
                        {
                            Input = AgentInput.Text(prompt),
                            ExpectedRunId = tab.ActiveRunId,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                runId = await _runtimeService.SendAsync(
                        thread,
                        executionOptions,
                        new AgentSendOptions { Input = AgentInput.Text(prompt) },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            tab.ActiveRunId = runId;
            thread.MarkStarted(DateTimeOffset.UtcNow);
            tab.HistoryLoaded = true;
            _commandContext.RefreshHeaderAndThreadWorkspace();
        }
        catch (Exception ex)
        {
            if (steer && !string.IsNullOrWhiteSpace(pendingSteerId))
            {
                _queueCoordinator.RemovePendingSteer(tab, pendingSteerId);
            }

            if (LogManager.IsInitialized && CodeAltaApp.UiLogger.IsEnabled(LogLevel.Error))
            {
                CodeAltaApp.UiLogger.Error(ex, $"Failed to send prompt for thread {thread.ThreadId}");
            }

            var restoredToDraft = false;
            if (_commandContext.IsThreadInputEmpty())
            {
                _commandContext.RestoreThreadInput(prompt);
                restoredToDraft = true;
            }

            tab.Timeline.RenderFailure(BuildPromptDispatchFailureMarkdown(ex.Message, prompt, restoredToDraft));
            _commandContext.SetThreadStatus(tab, $"Failed to send prompt: {ex.Message}", false, StatusTone.Error);
        }
    }

    private static string BuildPromptDispatchFailureMarkdown(string errorMessage, string prompt, bool restoredToDraft)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var normalizedPrompt = prompt.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        var builder = new System.Text.StringBuilder();
        builder.Append("Failed to send prompt: ").Append(errorMessage);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine(restoredToDraft
            ? "Prompt restored to the editor for retry."
            : "Prompt preserved below because the current editor draft is no longer empty.");
        builder.AppendLine();
        builder.AppendLine("Prompt:");

        foreach (var line in normalizedPrompt.Split('\n'))
        {
            builder.Append("> ");
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }
}
