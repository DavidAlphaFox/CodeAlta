using System.Text.Json;
using CodeAlta.App.State;
using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Presentation.Formatting;
using CodeAlta.Presentation.Timeline;
using CodeAlta.Presentation.Usage;
using CodeAlta.Views;
using XenoAtom.Logging;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.App;

internal sealed class SessionHistoryCoordinator
{
    private readonly SessionRuntimeService _runtimeService;
    private readonly Func<SessionViewDescriptor, OpenThreadState> _ensureThreadTab;
    private readonly Func<string, SessionViewDescriptor?> _findThread;
    private readonly Func<string, OpenThreadState?> _findOpenThread;
    private readonly Func<SessionViewDescriptor, bool> _canLoadHistory;
    private readonly Func<SessionViewDescriptor, OpenThreadState, SessionExecutionOptions> _buildExecutionOptions;
    private readonly Action<OpenThreadState, string, bool, StatusTone> _setThreadStatus;
    private readonly Action<OpenThreadState> _clearThreadStatus;
    private readonly Action<OpenThreadState> _resetThreadTab;
    private readonly Func<SessionViewDescriptor, OpenThreadState, AgentEvent, Task> _handleAgentEventAsync;
    private readonly Func<SessionViewDescriptor, Task> _persistThreadLocalStateAsync;
    private readonly Action<OpenThreadState> _notifySessionUsageChanged;
    private readonly Action<SessionViewDescriptor, OpenThreadState, IReadOnlyList<AgentEvent>> _projectLoadedHistory;
    private readonly Func<Func<Task>, Task> _dispatchToUiAsync;

    public SessionHistoryCoordinator(
        SessionRuntimeService runtimeService,
        Func<SessionViewDescriptor, OpenThreadState> ensureThreadTab,
        Func<string, SessionViewDescriptor?> findThread,
        Func<string, OpenThreadState?> findOpenThread,
        Func<SessionViewDescriptor, bool> canLoadHistory,
        Func<SessionViewDescriptor, OpenThreadState, SessionExecutionOptions> buildExecutionOptions,
        Action<OpenThreadState, string, bool, StatusTone> setThreadStatus,
        Action<OpenThreadState> clearThreadStatus,
        Action<OpenThreadState> resetThreadTab,
        Func<SessionViewDescriptor, OpenThreadState, AgentEvent, Task> handleAgentEventAsync,
        Func<SessionViewDescriptor, Task> persistThreadLocalStateAsync,
        Action<OpenThreadState>? notifySessionUsageChanged = null,
        Action<SessionViewDescriptor, OpenThreadState, IReadOnlyList<AgentEvent>>? projectLoadedHistory = null,
        Func<Func<Task>, Task>? dispatchToUiAsync = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeService);
        ArgumentNullException.ThrowIfNull(ensureThreadTab);
        ArgumentNullException.ThrowIfNull(findThread);
        ArgumentNullException.ThrowIfNull(findOpenThread);
        ArgumentNullException.ThrowIfNull(canLoadHistory);
        ArgumentNullException.ThrowIfNull(buildExecutionOptions);
        ArgumentNullException.ThrowIfNull(setThreadStatus);
        ArgumentNullException.ThrowIfNull(clearThreadStatus);
        ArgumentNullException.ThrowIfNull(resetThreadTab);
        ArgumentNullException.ThrowIfNull(handleAgentEventAsync);
        ArgumentNullException.ThrowIfNull(persistThreadLocalStateAsync);

        _runtimeService = runtimeService;
        _ensureThreadTab = ensureThreadTab;
        _findThread = findThread;
        _findOpenThread = findOpenThread;
        _canLoadHistory = canLoadHistory;
        _buildExecutionOptions = buildExecutionOptions;
        _setThreadStatus = setThreadStatus;
        _clearThreadStatus = clearThreadStatus;
        _resetThreadTab = resetThreadTab;
        _handleAgentEventAsync = handleAgentEventAsync;
        _persistThreadLocalStateAsync = persistThreadLocalStateAsync;
        _notifySessionUsageChanged = notifySessionUsageChanged ?? (static _ => { });
        _projectLoadedHistory = projectLoadedHistory ?? (static (_, _, _) => { });
        _dispatchToUiAsync = dispatchToUiAsync ?? (static action => action());
    }

    public async Task EnsureLoadedAsync(SessionViewDescriptor thread, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (!_canLoadHistory(thread))
        {
            return;
        }

        var tab = _ensureThreadTab(thread);
        var loadTask = GetOrStartLoadTask(tab, thread, cancellationToken);
        await loadTask;
    }

    public async Task LoadEarlierAsync(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var thread = _findThread(threadId);
        var tab = _findOpenThread(threadId);
        if (thread is null || tab is null || !tab.Timeline.HasLoadableTruncatedHistory)
        {
            return;
        }

        tab.Timeline.ReplaceTruncatedHistoryLoadButton();
        await Task.Run(
            () => RebuildAsync(
                thread,
                tab,
                loadOnlyFromLastUserPrompt: false,
                preferCachedHistory: true,
                CancellationToken.None));
    }

    public static bool CanLoadThreadHistory(SessionViewDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (thread.StartedAt is not null)
        {
            return true;
        }

        return thread.Status != WorkThreadStatus.Draft &&
               !string.IsNullOrWhiteSpace(thread.ThreadId);
    }

    public static ThreadHistoryLoadPlan CreateInitialLoadPlan(IReadOnlyList<AgentEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var startIndex = FindInitialStartIndex(history);
        if (startIndex <= 0 || startIndex >= history.Count)
        {
            return new ThreadHistoryLoadPlan(history, OmittedMessageCount: 0);
        }

        var pinnedPrefixIndexes = FindPinnedPrefixEventIndexes(history, startIndex);
        var pinnedPrefixIndexSet = pinnedPrefixIndexes.ToHashSet();
        var eventsToRender = pinnedPrefixIndexes
            .Select(index => history[index])
            .Concat(history.Skip(startIndex))
            .ToArray();
        return new ThreadHistoryLoadPlan(
            eventsToRender,
            CountRenderableMessages(history.Take(startIndex).Where((_, index) => !pinnedPrefixIndexSet.Contains(index))));
    }

    public static AgentSessionUsage? RecoverUsageFromHistory(IReadOnlyList<AgentEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        AgentSessionUsage? usage = null;
        foreach (var @event in history)
        {
            if (@event is AgentSessionUpdateEvent { Usage: { } updateUsage })
            {
                usage = SessionUsageAggregator.Merge(usage, updateUsage);
            }
        }

        return usage;
    }

    public static ModelProviderPreference? RecoverModelProviderPreferenceFromHistory(IReadOnlyList<AgentEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        for (var index = history.Count - 1; index >= 0; index--)
        {
            if (history[index] is AgentSessionUpdateEvent { Kind: AgentSessionUpdateKind.ModelChanged } update &&
                TryReadModelSelection(update, out var modelId, out var reasoningEffort))
            {
                return new ModelProviderPreference(
                    new ModelProviderId(update.ProviderId.Value),
                    modelId,
                    reasoningEffort);
            }
        }

        return null;
    }

    private static bool ApplyRecoveredModelProviderPreference(
        SessionViewDescriptor thread,
        OpenThreadState tab,
        ModelProviderPreference? preference)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(tab);

        if (preference is null)
        {
            return false;
        }

        var normalized = preference.Normalize();
        var changed = false;
        if (!string.Equals(tab.ProviderId.Value, normalized.ModelProviderId.Value, StringComparison.OrdinalIgnoreCase))
        {
            tab.ProviderId = normalized.ModelProviderId;
            changed = true;
        }

        if (!string.Equals(thread.ProviderKey, normalized.ModelProviderId.Value, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(thread.ProviderId, normalized.ModelProviderId.Value, StringComparison.OrdinalIgnoreCase))
        {
            thread.ProviderKey = normalized.ModelProviderId.Value;
            thread.ProviderId = normalized.ModelProviderId.Value;
            changed = true;
        }

        if (!string.Equals(tab.ModelId, normalized.ModelId, StringComparison.Ordinal))
        {
            tab.ModelId = normalized.ModelId;
            changed = true;
        }

        if (!string.Equals(thread.ModelId, normalized.ModelId, StringComparison.Ordinal))
        {
            thread.ModelId = normalized.ModelId;
            changed = true;
        }

        if (tab.ReasoningEffort != normalized.ReasoningEffort)
        {
            tab.ReasoningEffort = normalized.ReasoningEffort;
            changed = true;
        }

        if (thread.ReasoningEffort != normalized.ReasoningEffort)
        {
            thread.ReasoningEffort = normalized.ReasoningEffort;
            changed = true;
        }

        return changed;
    }

    private static bool TryReadModelSelection(
        AgentSessionUpdateEvent update,
        out string? modelId,
        out AgentReasoningEffort? reasoningEffort)
    {
        modelId = null;
        reasoningEffort = null;
        if (update.Details is not { ValueKind: JsonValueKind.Object } details)
        {
            return false;
        }

        if (details.TryGetProperty("modelId", out var modelProperty) &&
            modelProperty.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(modelProperty.GetString()))
        {
            modelId = modelProperty.GetString()!.Trim();
        }

        if (details.TryGetProperty("reasoningEffort", out var reasoningProperty) &&
            reasoningProperty.ValueKind == JsonValueKind.String &&
            Enum.TryParse<AgentReasoningEffort>(reasoningProperty.GetString(), ignoreCase: true, out var parsedReasoningEffort))
        {
            reasoningEffort = parsedReasoningEffort;
        }

        return !string.IsNullOrWhiteSpace(modelId) || reasoningEffort is not null;
    }

    private static IReadOnlyList<int> FindPinnedPrefixEventIndexes(IReadOnlyList<AgentEvent> history, int endIndex)
    {
        var latestSystemPromptIndex = -1;
        var latestModelChangedIndex = -1;
        for (var index = 0; index < endIndex; index++)
        {
            switch (history[index])
            {
                case AgentSystemPromptEvent:
                    latestSystemPromptIndex = index;
                    break;
                case AgentSessionUpdateEvent { Kind: AgentSessionUpdateKind.ModelChanged }:
                    latestModelChangedIndex = index;
                    break;
            }
        }

        return new[] { latestSystemPromptIndex, latestModelChangedIndex }
            .Where(static index => index >= 0)
            .Distinct()
            .Order()
            .ToArray();
    }

    public static int FindInitialStartIndex(IReadOnlyList<AgentEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var lastUserContentId = default(string);
        var lastUserIndex = -1;
        for (var index = history.Count - 1; index >= 0; index--)
        {
            if (TryGetUserContentId(history[index], out var contentId))
            {
                lastUserContentId = contentId;
                lastUserIndex = index;
                break;
            }
        }

        if (lastUserIndex <= 0 || string.IsNullOrWhiteSpace(lastUserContentId))
        {
            return 0;
        }

        var startIndex = lastUserIndex;
        while (startIndex > 0 &&
               TryGetUserContentId(history[startIndex - 1], out var previousContentId) &&
               string.Equals(previousContentId, lastUserContentId, StringComparison.Ordinal))
        {
            startIndex--;
        }

        return startIndex;
    }

    public static AgentSystemPromptEvent? FindPriorSystemPromptForFirstRenderedSystemPrompt(
        IReadOnlyList<AgentEvent> history,
        IReadOnlyList<AgentEvent> eventsToRender)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(eventsToRender);

        var firstRenderedSystemPrompt = eventsToRender.OfType<AgentSystemPromptEvent>().FirstOrDefault();
        if (firstRenderedSystemPrompt is null)
        {
            return null;
        }

        var firstRenderedIndex = -1;
        for (var index = 0; index < history.Count; index++)
        {
            if (ReferenceEquals(history[index], firstRenderedSystemPrompt))
            {
                firstRenderedIndex = index;
                break;
            }
        }

        if (firstRenderedIndex <= 0)
        {
            return null;
        }

        for (var index = firstRenderedIndex - 1; index >= 0; index--)
        {
            if (history[index] is AgentSystemPromptEvent systemPrompt)
            {
                return systemPrompt;
            }
        }

        return null;
    }

    public static int CountRenderableMessages(IEnumerable<AgentEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var contentKeys = new HashSet<string>(StringComparer.Ordinal);
        var activityIds = new HashSet<string>(StringComparer.Ordinal);
        var interactionIds = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        var hasPendingFileChangeRecap = false;

        foreach (var @event in history)
        {
            switch (@event)
            {
                case AgentContentDeltaEvent delta when ChatMarkdownFormatter.ShouldDisplayContentDelta(delta):
                    if (contentKeys.Add(ChatTimelineVisualFactory.CreateContentKey(delta.Kind, delta.ContentId)))
                    {
                        count++;
                    }

                    break;

                case AgentContentCompletedEvent completed when ShouldDisplayCompletedHistoryContent(completed):
                    if (contentKeys.Add(ChatTimelineVisualFactory.CreateContentKey(completed.Kind, completed.ContentId)))
                    {
                        count++;
                    }

                    break;

                case AgentPlanSnapshotEvent:
                    count++;
                    break;

                case AgentActivityEvent activity:
                    if (activity.Kind == AgentActivityKind.FileChange &&
                        activity.Phase is not (AgentActivityPhase.Failed or AgentActivityPhase.Canceled))
                    {
                        hasPendingFileChangeRecap = true;
                    }

                    if (ChatMarkdownFormatter.ShouldDisplayActivity(activity) && activityIds.Add(activity.ActivityId))
                    {
                        count++;
                    }

                    break;

                case AgentRawEvent raw when ChatMarkdownFormatter.ShouldDisplayRawEvent(raw):
                    count++;
                    break;

                case AgentPermissionRequest permissionRequest when interactionIds.Add(permissionRequest.InteractionId):
                    count++;
                    break;

                case AgentUserInputRequest userInputRequest when interactionIds.Add(userInputRequest.InteractionId):
                    count++;
                    break;

                case AgentInteractionEvent interaction when interactionIds.Add(interaction.InteractionId):
                    count++;
                    break;

                case AgentSystemPromptEvent:
                    count++;
                    break;

                case AgentSessionUpdateEvent update:
                    if (update.Kind == AgentSessionUpdateKind.DiffUpdated)
                    {
                        hasPendingFileChangeRecap = true;
                    }

                    if (update.Kind is AgentSessionUpdateKind.Idle or AgentSessionUpdateKind.Shutdown)
                    {
                        if (hasPendingFileChangeRecap)
                        {
                            count++;
                            hasPendingFileChangeRecap = false;
                        }
                    }

                    if (update.Kind != AgentSessionUpdateKind.Idle && ChatMarkdownFormatter.ShouldDisplaySessionUpdate(update))
                    {
                        count++;
                    }

                    break;

                case AgentErrorEvent:
                    count++;
                    break;
            }
        }

        return count;
    }

    private Task GetOrStartLoadTask(
        OpenThreadState tab,
        SessionViewDescriptor thread,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(thread);

        if (tab.HistoryLoaded)
        {
            return Task.CompletedTask;
        }

        if (tab.HistoryLoadTask is { } existingTask)
        {
            return existingTask.WaitAsync(cancellationToken);
        }

        var loadTask = Task.Run(() => LoadCoreAsync(thread, tab, cancellationToken));
        tab.HistoryLoadTask = loadTask;
        return loadTask.WaitAsync(cancellationToken);
    }

    private async Task LoadCoreAsync(
        SessionViewDescriptor thread,
        OpenThreadState tab,
        CancellationToken cancellationToken)
    {
        await RebuildAsync(
                thread,
                tab,
                loadOnlyFromLastUserPrompt: true,
                preferCachedHistory: false,
                cancellationToken)
            ;
    }

    private async Task RebuildAsync(
        SessionViewDescriptor thread,
        OpenThreadState tab,
        bool loadOnlyFromLastUserPrompt,
        bool preferCachedHistory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentEvent>? cachedHistory = null;
        SessionExecutionOptions? executionOptions = null;
        try
        {
            await _dispatchToUiAsync(
                    () =>
                    {
                        tab.HistoryLoading = true;
                        _setThreadStatus(
                            tab,
                            loadOnlyFromLastUserPrompt
                                ? $"Loading session '{thread.Title}'..."
                                : $"Loading previous messages from '{thread.Title}'...",
                            true,
                            StatusTone.Info);
                        if (preferCachedHistory && tab.HistoryEvents is { Count: > 0 } historyEvents)
                        {
                            cachedHistory = historyEvents.ToList();
                        }

                        executionOptions = _buildExecutionOptions(thread, tab);
                        return Task.CompletedTask;
                    })
                .ConfigureAwait(false);

            var history = await GetHistoryAsync(thread, cachedHistory, executionOptions!, cancellationToken).ConfigureAwait(false);
            thread.MessageCount = CountRenderableMessages(history);
            await _persistThreadLocalStateAsync(thread).ConfigureAwait(false);
            var recoveredUsage = RecoverUsageFromHistory(history);
            var recoveredModelPreference = RecoverModelProviderPreferenceFromHistory(history);
            var plan = loadOnlyFromLastUserPrompt
                ? CreateInitialLoadPlan(history)
                : new ThreadHistoryLoadPlan(history, OmittedMessageCount: 0);
            var recoveredModelPreferenceChanged = false;
            await _dispatchToUiAsync(
                    async () =>
                    {
                        recoveredModelPreferenceChanged = ApplyRecoveredModelProviderPreference(thread, tab, recoveredModelPreference);
                        tab.HistoryEvents = history.ToList();
                        var previousUsage = tab.Usage;
                        _resetThreadTab(tab);
                        tab.Usage = recoveredUsage;
                        var usageChanged = !Equals(previousUsage, recoveredUsage);

                        tab.Session.LastRenderedSystemPromptEvent = FindPriorSystemPromptForFirstRenderedSystemPrompt(history, plan.EventsToRender);
                        DocumentFlowItem? truncatedHistoryItem = null;
                        if (plan.OmittedMessageCount > 0)
                        {
                            truncatedHistoryItem = tab.Timeline.CreateTruncatedHistoryItem(
                                plan.OmittedMessageCount,
                                () => _ = LoadEarlierAsync(thread.ThreadId));
                        }

                        tab.Timeline.BeginBufferedHistoryLoad();
                        var renderedEventCount = 0;
                        foreach (var @event in plan.EventsToRender)
                        {
                            await _handleAgentEventAsync(thread, tab, @event);
                            renderedEventCount++;
                            if (renderedEventCount % 25 == 0)
                            {
                                await Task.Yield();
                            }
                        }

                        tab.Timeline.CompleteInitialBufferedHistory(truncatedHistoryItem);
                        tab.Timeline.FlushBufferedHistoryItems();
                        tab.HistoryLoaded = true;
                        if (usageChanged)
                        {
                            _notifySessionUsageChanged(tab);
                        }

                        _clearThreadStatus(tab);
                    })
                .ConfigureAwait(false);
            if (recoveredModelPreferenceChanged)
            {
                await _persistThreadLocalStateAsync(thread).ConfigureAwait(false);
            }

            _projectLoadedHistory(thread, tab, plan.EventsToRender);
        }
        catch (Exception ex)
        {
            CodeAltaApp.UiLogger.Error(ex, $"Failed to load history for thread {thread.ThreadId}");

            await _dispatchToUiAsync(
                    () =>
                    {
                        _resetThreadTab(tab);
                        tab.Timeline.FlushBufferedHistoryItems();
                        tab.Timeline.RenderFailure($"Failed to load history: {ex.Message}");
                        _setThreadStatus(tab, $"Failed to load '{thread.Title}': {ex.Message}", false, StatusTone.Error);
                        return Task.CompletedTask;
                    })
                .ConfigureAwait(false);
        }
        finally
        {
            await _dispatchToUiAsync(
                    () =>
                    {
                        tab.HistoryLoading = false;
                        tab.HistoryLoadTask = null;
                        tab.Timeline.ClearBufferedHistory();
                        return Task.CompletedTask;
                    })
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(
        SessionViewDescriptor thread,
        IReadOnlyList<AgentEvent>? cachedHistory,
        SessionExecutionOptions executionOptions,
        CancellationToken cancellationToken)
    {
        if (cachedHistory is not null)
        {
            return cachedHistory;
        }

        ArgumentNullException.ThrowIfNull(executionOptions);
        try
        {
            await _runtimeService.EnsureCoordinatorSessionAsync(thread, executionOptions, cancellationToken).ConfigureAwait(false);
            return (await _runtimeService.GetHistoryAsync(thread.ThreadId, cancellationToken).ConfigureAwait(false)).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            if (await _runtimeService.TryReadStoredHistoryAsync(thread, cancellationToken).ConfigureAwait(false) is { } storedHistory)
            {
                return storedHistory.ToList();
            }

            throw;
        }
    }

    private static bool ShouldDisplayCompletedHistoryContent(AgentContentCompletedEvent completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        if (!ChatMarkdownFormatter.ShouldDisplayCompletedContent(completed))
        {
            return false;
        }

        return completed.Kind != AgentContentKind.Assistant || !string.IsNullOrWhiteSpace(completed.Content);
    }

    private static bool TryGetUserContentId(AgentEvent @event, out string? contentId)
    {
        switch (@event)
        {
            case AgentContentDeltaEvent { Kind: AgentContentKind.User } delta:
                contentId = delta.ContentId;
                return !string.IsNullOrWhiteSpace(contentId);
            case AgentContentCompletedEvent { Kind: AgentContentKind.User } completed:
                contentId = completed.ContentId;
                return !string.IsNullOrWhiteSpace(contentId);
            default:
                contentId = null;
                return false;
        }
    }
}
