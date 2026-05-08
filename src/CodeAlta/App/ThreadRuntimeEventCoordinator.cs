using CodeAlta.App.State;
using CodeAlta.App.Events;
using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Presentation.Shell;
using CodeAlta.Views;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal sealed class ThreadRuntimeEventCoordinator
{
    private readonly ShellStateStore _stateStore;
    private readonly Func<string, OpenThreadState?> _findOpenThread;
    private readonly Func<string, bool> _isSelectedThread;
    private readonly IShellStatusPort _statusPort;
    private readonly Func<OpenThreadState, CancellationToken, Task> _drainQueuedPromptAsync;
    private readonly IProjectFileSearchService _projectFileSearchService;
    private readonly IPluginAgentEventObserver _pluginAgentEventObserver;
    private readonly FrontendEventPublisher? _frontendEvents;
    private readonly ThreadRuntimeStateReducer _stateReducer;
    private readonly ThreadRuntimeTimelineRenderer _timelineRenderer;

    public ThreadRuntimeEventCoordinator(
        ShellStateStore stateStore,
        Func<string, OpenThreadState?> findOpenThread,
        Func<bool> getAutoApproveEnabled,
        Func<string, bool> isSelectedThread,
        IShellStatusPort statusPort,
        Func<OpenThreadState, CancellationToken, Task> drainQueuedPromptAsync,
        IProjectFileSearchService projectFileSearchService,
        IPluginAgentEventObserver? pluginAgentEventObserver = null,
        FrontendEventPublisher? frontendEvents = null)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(findOpenThread);
        ArgumentNullException.ThrowIfNull(getAutoApproveEnabled);
        ArgumentNullException.ThrowIfNull(isSelectedThread);
        ArgumentNullException.ThrowIfNull(statusPort);
        ArgumentNullException.ThrowIfNull(drainQueuedPromptAsync);
        ArgumentNullException.ThrowIfNull(projectFileSearchService);

        _stateStore = stateStore;
        _findOpenThread = findOpenThread;
        _isSelectedThread = isSelectedThread;
        _statusPort = statusPort;
        _drainQueuedPromptAsync = drainQueuedPromptAsync;
        _projectFileSearchService = projectFileSearchService;
        _pluginAgentEventObserver = pluginAgentEventObserver ?? new PluginAgentEventObserver(null);
        _frontendEvents = frontendEvents;
        _stateReducer = new ThreadRuntimeStateReducer();
        _timelineRenderer = new ThreadRuntimeTimelineRenderer(getAutoApproveEnabled);
    }

    public void ApplyRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);

        var thread = FindThread(runtimeEvent.ThreadId);
        if (thread is null)
        {
            return;
        }

        var tab = _findOpenThread(thread.ThreadId);
        var reduction = _stateReducer.ReduceRuntimeEvent(
            thread,
            tab,
            runtimeEvent,
            _isSelectedThread(thread.ThreadId));

        if (tab is not null)
        {
            switch (runtimeEvent)
            {
                case WorkThreadAgentEvent agentEvent:
                    tab.HistoryEvents ??= [];
                    tab.HistoryEvents.Add(agentEvent.Event);
                    TryRenderInteraction(tab, () => _timelineRenderer.RenderAgentEvent(tab, agentEvent.Event), "agent event");
                    ProjectPluginThreadEvents(thread, tab, tab.HistoryEvents, isReplay: false);
                    _frontendEvents?.Publish(new RuntimeTimelineChangedEvent(thread.ThreadId));
                    break;

                case WorkThreadHostEvent hostEvent:
                    TryRenderInteraction(tab, () => _timelineRenderer.RenderHostEvent(tab, hostEvent), "host event");
                    _frontendEvents?.Publish(new RuntimeTimelineChangedEvent(thread.ThreadId));
                    break;
            }
        }

        if (runtimeEvent is WorkThreadAgentEvent agentRuntimeEvent)
        {
            InvalidateProjectFileSearchIfNeeded(thread, agentRuntimeEvent.Event);
            ObservePluginAgentEvent(thread, agentRuntimeEvent.Event);
        }

        ApplyReduction(tab, reduction);
    }

    public void HandleAgentEvent(WorkThreadDescriptor thread, OpenThreadState tab, AgentEvent @event)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(@event);

        var reduction = _stateReducer.ReduceAgentEvent(thread, tab, @event, _isSelectedThread(thread.ThreadId));
        if (!tab.HistoryLoading)
        {
            tab.HistoryEvents ??= [];
            tab.HistoryEvents.Add(@event);
        }

        TryRenderInteraction(tab, () => _timelineRenderer.RenderAgentEvent(tab, @event), "agent event");
        ProjectPluginThreadEvents(thread, tab, tab.HistoryEvents ?? [@event], isReplay: tab.HistoryLoading);
        _frontendEvents?.Publish(new RuntimeTimelineChangedEvent(thread.ThreadId));
        InvalidateProjectFileSearchIfNeeded(thread, @event);
        ObservePluginAgentEvent(thread, @event);
        ApplyReduction(tab, reduction);
    }

    public void TryRenderInteraction(OpenThreadState tab, Action action, string context)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (LogManager.IsInitialized && CodeAltaApp.UiLogger.IsEnabled(LogLevel.Error))
            {
                CodeAltaApp.UiLogger.Error(ex, $"Failed to render thread {context}");
            }

            _statusPort.SetShellStatus(new ShellStatusUpdate($"Failed to render thread {context}: {ex.Message}", false, StatusTone.Error));
            tab.Timeline.ClearPendingAssistant();
        }
    }

    private void ObservePluginAgentEvent(WorkThreadDescriptor thread, AgentEvent @event)
        => global::CodeAlta.CodeAltaTaskMonitor.Observe(
            _pluginAgentEventObserver.ObserveAgentEventAsync(thread, @event),
            $"Plugin agent event observer for thread {thread.ThreadId}");

    private void ProjectPluginThreadEvents(
        WorkThreadDescriptor thread,
        OpenThreadState tab,
        IReadOnlyList<AgentEvent> events,
        bool isReplay)
        => global::CodeAlta.CodeAltaTaskMonitor.Observe(
            ProjectPluginThreadEventsAsync(thread, tab, events.ToArray(), isReplay),
            $"Plugin thread event projection for thread {thread.ThreadId}");

    private async Task ProjectPluginThreadEventsAsync(
        WorkThreadDescriptor thread,
        OpenThreadState tab,
        IReadOnlyList<AgentEvent> events,
        bool isReplay)
    {
        var result = await _pluginAgentEventObserver.ProjectThreadEventsAsync(thread, tab, events, isReplay);
        if (result.Events.Count == 0)
        {
            return;
        }

        foreach (var derivedEvent in result.Events)
        {
            var changed = tab.PluginTransientEvents.Apply(derivedEvent);
            if (!changed)
            {
                continue;
            }

            if (derivedEvent.Remove)
            {
                TryRenderInteraction(tab, () => tab.Timeline.RemovePluginProjection(derivedEvent.EventId), "plugin projection");
                continue;
            }

            var projection = tab.PluginTransientEvents.Snapshot.FirstOrDefault(item => string.Equals(item.EventId, derivedEvent.EventId, StringComparison.Ordinal));
            if (projection is null)
            {
                continue;
            }

            TryRenderInteraction(
                tab,
                () => tab.Timeline.UpsertPluginProjection(
                    projection.EventId,
                    projection.Timestamp ?? DateTimeOffset.UtcNow,
                    projection.Markdown,
                    projection.RenderTarget),
                "plugin projection");
        }

        _frontendEvents?.Publish(new RuntimeTimelineChangedEvent(thread.ThreadId));
    }

    public static bool ShouldPromoteAgentEventToThinking(AgentEvent @event)
        => ThreadRuntimeStateReducer.ShouldPromoteAgentEventToThinking(@event);

    public static bool ShouldApplyShellChromeProjectionAfterRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent)
        => ThreadRuntimeStateReducer.ShouldApplyShellChromeProjectionAfterRuntimeEvent(runtimeEvent);

    public static string SummarizeContent(string content)
        => ThreadRuntimeStateReducer.SummarizeContent(content);

    private WorkThreadDescriptor? FindThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return _stateStore.Snapshot.Threads.FirstOrDefault(thread => string.Equals(thread.ThreadId, threadId, StringComparison.OrdinalIgnoreCase));
    }

    private void InvalidateProjectFileSearchIfNeeded(WorkThreadDescriptor thread, AgentEvent @event)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(@event);

        if (!ShouldInvalidateProjectFileSearch(@event) ||
            string.IsNullOrWhiteSpace(thread.WorkingDirectory))
        {
            return;
        }

        _ = InvalidateProjectFileSearchAsync(thread.WorkingDirectory);
    }

    private async Task InvalidateProjectFileSearchAsync(string projectRoot)
    {
        try
        {
            await _projectFileSearchService.InvalidateAsync(projectRoot, ProjectFileInvalidationReason.FileSystemWrite);
        }
        catch
        {
        }
    }

    private static bool ShouldInvalidateProjectFileSearch(AgentEvent @event)
        => @event switch
        {
            AgentActivityEvent { Kind: AgentActivityKind.FileChange } => true,
            AgentSessionUpdateEvent { Kind: AgentSessionUpdateKind.DiffUpdated } => true,
            _ => false,
        };

    private void ApplyReduction(OpenThreadState? tab, ThreadRuntimeReductionResult reduction)
    {
        var focusPromptAfterReduction = false;
        if (tab is not null)
        {
            if (reduction.ClearThreadStatus)
            {
                _statusPort.ClearThreadStatus(tab);
                _frontendEvents?.Publish(new ThreadStatusChangedEvent(tab.Thread.ThreadId));
                focusPromptAfterReduction = _isSelectedThread(tab.Thread.ThreadId);
            }

            if (reduction.ThreadStatus is { } status)
            {
                _statusPort.SetThreadStatus(tab, new ThreadStatusUpdate(status.Message, status.ShowSpinner, status.Tone));
                _frontendEvents?.Publish(new ThreadStatusChangedEvent(tab.Thread.ThreadId));
            }

            if (reduction.DrainQueuedPrompt)
            {
                global::CodeAlta.CodeAltaTaskMonitor.Observe(
                    _drainQueuedPromptAsync(tab, CancellationToken.None),
                    $"Queued prompt drain for thread {tab.Thread.ThreadId}");
            }
        }

        if (reduction.RefreshPromptStrip && tab is not null)
        {
            _frontendEvents?.Publish(new QueuedPromptListChangedEvent(tab.Thread.ThreadId));
        }

        if (reduction.ApplySessionUsageProjection && tab is not null)
        {
            _frontendEvents?.Publish(new SessionUsageChangedEvent(tab.Thread.ThreadId));
        }

        if (reduction.ApplyShellChromeProjection)
        {
            _frontendEvents?.Publish(new ShellChromeChangedEvent());
        }

        if (focusPromptAfterReduction)
        {
            _frontendEvents?.Publish(new PromptFocusRequestedEvent());
        }
    }
}
