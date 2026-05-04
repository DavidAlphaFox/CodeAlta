using CodeAlta.App.State;
using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Presentation.Shell;
using CodeAlta.Search;
using CodeAlta.Views;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal sealed class ThreadRuntimeEventCoordinator
{
    private readonly Func<string, WorkThreadDescriptor?> _findThread;
    private readonly Func<string, OpenThreadState?> _findOpenThread;
    private readonly Func<string, bool> _isSelectedThread;
    private readonly Action _invalidateSelectedSessionUsage;
    private readonly Action _refreshShellChrome;
    private readonly Action<string, bool, StatusTone> _setShellStatus;
    private readonly Action<OpenThreadState, string, bool, StatusTone> _setThreadStatus;
    private readonly Action<OpenThreadState> _clearThreadStatus;
    private readonly Action _refreshQueuedPromptList;
    private readonly Func<OpenThreadState, CancellationToken, Task> _drainQueuedPromptAsync;
    private readonly IProjectFileSearchService _projectFileSearchService;
    private readonly PluginHostBridge? _pluginHostBridge;
    private readonly ThreadRuntimeStateReducer _stateReducer;
    private readonly ThreadRuntimeTimelineRenderer _timelineRenderer;

    public ThreadRuntimeEventCoordinator(
        Func<string, WorkThreadDescriptor?> findThread,
        Func<string, OpenThreadState?> findOpenThread,
        Func<bool> getAutoApproveEnabled,
        Func<string, bool> isSelectedThread,
        Action invalidateSelectedSessionUsage,
        Action refreshShellChrome,
        Action<string, bool, StatusTone> setShellStatus,
        Action<OpenThreadState, string, bool, StatusTone> setThreadStatus,
        Action<OpenThreadState> clearThreadStatus,
        Action refreshQueuedPromptList,
        Func<OpenThreadState, CancellationToken, Task> drainQueuedPromptAsync,
        IProjectFileSearchService projectFileSearchService,
        PluginHostBridge? pluginHostBridge = null)
    {
        ArgumentNullException.ThrowIfNull(findThread);
        ArgumentNullException.ThrowIfNull(findOpenThread);
        ArgumentNullException.ThrowIfNull(getAutoApproveEnabled);
        ArgumentNullException.ThrowIfNull(isSelectedThread);
        ArgumentNullException.ThrowIfNull(invalidateSelectedSessionUsage);
        ArgumentNullException.ThrowIfNull(refreshShellChrome);
        ArgumentNullException.ThrowIfNull(setShellStatus);
        ArgumentNullException.ThrowIfNull(setThreadStatus);
        ArgumentNullException.ThrowIfNull(clearThreadStatus);
        ArgumentNullException.ThrowIfNull(refreshQueuedPromptList);
        ArgumentNullException.ThrowIfNull(drainQueuedPromptAsync);
        ArgumentNullException.ThrowIfNull(projectFileSearchService);

        _findThread = findThread;
        _findOpenThread = findOpenThread;
        _isSelectedThread = isSelectedThread;
        _invalidateSelectedSessionUsage = invalidateSelectedSessionUsage;
        _refreshShellChrome = refreshShellChrome;
        _setShellStatus = setShellStatus;
        _setThreadStatus = setThreadStatus;
        _clearThreadStatus = clearThreadStatus;
        _refreshQueuedPromptList = refreshQueuedPromptList;
        _drainQueuedPromptAsync = drainQueuedPromptAsync;
        _projectFileSearchService = projectFileSearchService;
        _pluginHostBridge = pluginHostBridge;
        _stateReducer = new ThreadRuntimeStateReducer();
        _timelineRenderer = new ThreadRuntimeTimelineRenderer(getAutoApproveEnabled);
    }

    public void ApplyRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);

        var thread = _findThread(runtimeEvent.ThreadId);
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
                    tab.HistoryEvents?.Add(agentEvent.Event);
                    TryRenderInteraction(tab, () => _timelineRenderer.RenderAgentEvent(tab, agentEvent.Event), "agent event");
                    break;

                case WorkThreadHostEvent hostEvent:
                    TryRenderInteraction(tab, () => _timelineRenderer.RenderHostEvent(tab, hostEvent), "host event");
                    break;
            }
        }

        if (runtimeEvent is WorkThreadAgentEvent agentRuntimeEvent)
        {
            InvalidateProjectFileSearchIfNeeded(thread, agentRuntimeEvent.Event);
            _ = ObservePluginAgentEventAsync(thread, agentRuntimeEvent.Event);
        }

        ApplyReduction(tab, reduction);
    }

    public void HandleAgentEvent(WorkThreadDescriptor thread, OpenThreadState tab, AgentEvent @event)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(@event);

        var reduction = _stateReducer.ReduceAgentEvent(thread, tab, @event, _isSelectedThread(thread.ThreadId));
        TryRenderInteraction(tab, () => _timelineRenderer.RenderAgentEvent(tab, @event), "agent event");
        InvalidateProjectFileSearchIfNeeded(thread, @event);
        _ = ObservePluginAgentEventAsync(thread, @event);
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

            _setShellStatus($"Failed to render thread {context}: {ex.Message}", false, StatusTone.Error);
            tab.Timeline.ClearPendingAssistant();
        }
    }

    private async Task ObservePluginAgentEventAsync(WorkThreadDescriptor thread, AgentEvent @event)
    {
        if (_pluginHostBridge is null)
        {
            return;
        }

        try
        {
            await _pluginHostBridge.ObserveAgentEventAsync(thread, @event);
        }
        catch (Exception ex)
        {
            if (LogManager.IsInitialized && CodeAltaApp.UiLogger.IsEnabled(LogLevel.Error))
            {
                CodeAltaApp.UiLogger.Error(ex, $"Plugin agent event observer failed for thread {thread.ThreadId}");
            }
        }
    }

    public static bool ShouldPromoteAgentEventToThinking(AgentEvent @event)
        => ThreadRuntimeStateReducer.ShouldPromoteAgentEventToThinking(@event);

    public static bool ShouldRefreshShellChromeAfterRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent)
        => ThreadRuntimeStateReducer.ShouldRefreshShellChromeAfterRuntimeEvent(runtimeEvent);

    public static string SummarizeContent(string content)
        => ThreadRuntimeStateReducer.SummarizeContent(content);

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
        if (tab is not null)
        {
            if (reduction.ClearThreadStatus)
            {
                _clearThreadStatus(tab);
            }

            if (reduction.ThreadStatus is { } status)
            {
                _setThreadStatus(tab, status.Message, status.ShowSpinner, status.Tone);
            }

            if (reduction.DrainQueuedPrompt)
            {
                _ = _drainQueuedPromptAsync(tab, CancellationToken.None);
            }
        }

        if (reduction.RefreshPromptStrip)
        {
            _refreshQueuedPromptList();
        }

        if (reduction.InvalidateSelectedSessionUsage)
        {
            _invalidateSelectedSessionUsage();
        }

        if (reduction.RefreshShellChrome)
        {
            _refreshShellChrome();
        }
    }
}
