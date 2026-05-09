using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.App.Events;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Orchestration.Runtime.Plugins;
using CodeAlta.Presentation.Prompting;
using CodeAlta.Presentation.Timeline;
using CodeAlta.Threading;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ThreadRuntimeEventCoordinatorTests
{
    [TestMethod]
    public void ShouldPromoteAgentEventToThinking_IgnoresToolOutputDeltas()
    {
        var delta = new AgentContentDeltaEvent(
            AgentBackendIds.Codex,
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            AgentContentKind.ToolOutput,
            "tool-1",
            "activity-1",
            "line 1");

        Assert.IsFalse(ThreadRuntimeEventCoordinator.ShouldPromoteAgentEventToThinking(delta));
    }

    [TestMethod]
    public void ShouldApplyShellChromeProjectionAfterRuntimeEvent_IgnoresToolOutputDeltas()
    {
        var runtimeEvent = new WorkThreadAgentEvent(
            "thread-1",
            new AgentContentDeltaEvent(
                AgentBackendIds.Codex,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentContentKind.ToolOutput,
                "tool-1",
                "activity-1",
                "line 1"));

        Assert.IsFalse(ThreadRuntimeEventCoordinator.ShouldApplyShellChromeProjectionAfterRuntimeEvent(runtimeEvent));
    }

    [TestMethod]
    public void ShouldApplyShellChromeProjectionAfterRuntimeEvent_RefreshesForAssistantCompletion()
    {
        var runtimeEvent = new WorkThreadAgentEvent(
            "thread-1",
            new AgentContentCompletedEvent(
                AgentBackendIds.Codex,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentContentKind.Assistant,
                "assistant-1",
                null,
                "Final answer"));

        Assert.IsTrue(ThreadRuntimeEventCoordinator.ShouldApplyShellChromeProjectionAfterRuntimeEvent(runtimeEvent));
    }

    [TestMethod]
    public void ShouldApplyShellChromeProjectionAfterRuntimeEvent_RefreshesForQueueEvents()
    {
        var runtimeEvent = new WorkThreadQueueRuntimeEvent(
            "thread-1",
            DateTimeOffset.UtcNow,
            QueuedPromptCount: 1,
            QueueItemId: "queue-1",
            PromptPreview: "queued prompt",
            IsEnqueued: true);

        Assert.IsTrue(ThreadRuntimeEventCoordinator.ShouldApplyShellChromeProjectionAfterRuntimeEvent(runtimeEvent));
    }

    [TestMethod]
    public void ApplyRuntimeEvent_ForwardsAgentEventsToPluginObserver()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var observer = new RecordingPluginAgentEventObserver();
        var coordinator = CreateCoordinator(thread, tab, pluginAgentEventObserver: observer);
        var agentEvent = new AgentContentCompletedEvent(
            AgentBackendIds.Copilot,
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            AgentContentKind.Assistant,
            "assistant-1",
            null,
            "Final answer");

        coordinator.ApplyRuntimeEvent(new WorkThreadAgentEvent(thread.ThreadId, agentEvent));

        Assert.AreSame(thread, observer.ObservedThread);
        Assert.AreSame(agentEvent, observer.ObservedEvent);
    }

    [TestMethod]
    public async Task HandleAgentEventAsync_ReplayProjectsOnlyRenderedHistoryEvents()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        tab.HistoryLoading = true;
        var hiddenEvent = new AgentContentCompletedEvent(
            AgentBackendIds.Codex,
            "session-1",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            null,
            AgentContentKind.Assistant,
            "hidden-assistant",
            null,
            "Earlier answer");
        var displayedEvent = new AgentContentCompletedEvent(
            AgentBackendIds.Codex,
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            AgentContentKind.Assistant,
            "displayed-assistant",
            null,
            "Displayed answer");
        tab.HistoryEvents = [hiddenEvent, displayedEvent];
        var observer = new RecordingPluginAgentEventObserver();
        var coordinator = CreateCoordinator(thread, tab, pluginAgentEventObserver: observer);

        await coordinator.HandleAgentEventAsync(thread, tab, displayedEvent);
        await observer.WaitForProjectionAsync();

        CollectionAssert.AreEqual(new[] { displayedEvent }, observer.ProjectedEvents?.ToArray());
        Assert.IsTrue(observer.ProjectedIsReplay);
    }

    [TestMethod]
    public void ApplyRuntimeEvent_AfterReplayProjectsVisibleHistoryPlusLiveEvents()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var hiddenEvent = new AgentContentCompletedEvent(
            AgentBackendIds.Codex,
            "session-1",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            null,
            AgentContentKind.Assistant,
            "hidden-assistant",
            null,
            "Earlier answer");
        var displayedEvent = new AgentContentCompletedEvent(
            AgentBackendIds.Codex,
            "session-1",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            AgentContentKind.Assistant,
            "displayed-assistant",
            null,
            "Displayed answer");
        var liveEvent = new AgentContentCompletedEvent(
            AgentBackendIds.Codex,
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            AgentContentKind.Assistant,
            "live-assistant",
            null,
            "Live answer");
        tab.HistoryEvents = [hiddenEvent, displayedEvent];
        tab.RenderedHistoryEvents.Add(displayedEvent);
        var observer = new RecordingPluginAgentEventObserver();
        var coordinator = CreateCoordinator(thread, tab, pluginAgentEventObserver: observer);

        coordinator.ApplyRuntimeEvent(new WorkThreadAgentEvent(thread.ThreadId, liveEvent));
        observer.WaitForProjectionAsync().GetAwaiter().GetResult();

        CollectionAssert.AreEqual(new AgentEvent[] { displayedEvent, liveEvent }, observer.ProjectedEvents?.ToArray());
        Assert.IsFalse(observer.ProjectedIsReplay);
    }

    [TestMethod]
    public void HandleAgentEvent_KeepsManualCompactionStatusWhileProgressEventsArrive()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        tab.PendingManualCompaction = true;
        tab.StatusMessage = $"Compacting '{thread.Title}'...";
        tab.StatusBusy = true;
        tab.StatusTone = StatusTone.Info;
        tab.HasCustomStatus = true;

        var coordinator = CreateCoordinator(thread, tab);
        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentActivityEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentActivityKind.ToolCall,
                AgentActivityPhase.Started,
                "activity-1",
                null,
                "compact",
                "Compacting session..."));

        Assert.AreEqual($"Compacting '{thread.Title}'...", tab.StatusMessage);
        Assert.IsTrue(tab.StatusBusy);
        Assert.AreEqual(StatusTone.Info, tab.StatusTone);
    }

    [TestMethod]
    public void ApplyRuntimeEvent_HostCompactionCompletionClearsPendingManualCompactionStatus()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        tab.PendingManualCompaction = true;
        tab.StatusMessage = $"Compacting '{thread.Title}'...";
        tab.StatusBusy = true;
        tab.StatusTone = StatusTone.Info;
        tab.HasCustomStatus = true;

        var coordinator = CreateCoordinator(thread, tab);
        coordinator.ApplyRuntimeEvent(
            new WorkThreadHostEvent(
                thread.ThreadId,
                DateTimeOffset.UtcNow,
                AgentSessionUpdateKind.CompactionCompleted,
                "Manual compaction completed."));

        Assert.IsFalse(tab.PendingManualCompaction);
        Assert.IsFalse(tab.HasCustomStatus);
        Assert.IsFalse(tab.StatusBusy);
        Assert.IsNull(tab.StatusMessage);
    }

    [TestMethod]
    public void RenderAgentEvent_SystemPromptChangeAddsPromptDiffSection()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var renderer = new ThreadRuntimeTimelineRenderer(static () => false);
        var timestamp = DateTimeOffset.UtcNow;
        var initial = CreateSystemPromptEvent(timestamp, "sha256:old", "system\nold", "developer", "initial");
        var changed = CreateSystemPromptEvent(timestamp.AddSeconds(1), "sha256:new", "system\nnew", "developer\nmore", "changed");

        renderer.RenderAgentEvent(tab, initial);
        renderer.RenderAgentEvent(tab, changed);

        Assert.AreSame(changed, tab.Session.LastRenderedSystemPromptEvent);
        Assert.AreEqual(2, tab.Timeline.Flow.Items.Count);
        var firstStack = GetChatCardStack(tab.Timeline.Flow.Items[0]);
        var changedStack = GetChatCardStack(tab.Timeline.Flow.Items[1]);

        Assert.AreEqual(2, firstStack.Children.Count);
        Assert.IsInstanceOfType<Collapsible>(firstStack.Children[1]);
        Assert.AreEqual(3, changedStack.Children.Count);
        Assert.IsInstanceOfType<Collapsible>(changedStack.Children[1]);
        Assert.IsInstanceOfType<Collapsible>(changedStack.Children[2]);
    }

    [TestMethod]
    public void RenderAgentEvent_SystemPromptChangeUsesSeededPriorPromptForDiffSection()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var renderer = new ThreadRuntimeTimelineRenderer(static () => false);
        var timestamp = DateTimeOffset.UtcNow;
        var initial = CreateSystemPromptEvent(timestamp, "sha256:old", "system\nold", "developer", "initial");
        var changed = CreateSystemPromptEvent(timestamp.AddSeconds(1), "sha256:new", "system\nnew", "developer", "changed");
        tab.Session.LastRenderedSystemPromptEvent = initial;

        renderer.RenderAgentEvent(tab, changed);

        Assert.AreSame(changed, tab.Session.LastRenderedSystemPromptEvent);
        Assert.AreEqual(1, tab.Timeline.Flow.Items.Count);
        var changedStack = GetChatCardStack(tab.Timeline.Flow.Items[0]);
        Assert.AreEqual(3, changedStack.Children.Count);
        Assert.IsInstanceOfType<Collapsible>(changedStack.Children[1]);
        Assert.IsInstanceOfType<Collapsible>(changedStack.Children[2]);
    }

    [TestMethod]
    public void HandleAgentEvent_RemovesPendingSteerOnFirstLiveUserContentOnly()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        tab.PendingSteers.Add(new PendingSteerPrompt("First steer"));
        tab.PendingSteers.Add(new PendingSteerPrompt("Second steer"));

        var coordinator = CreateCoordinator(thread, tab);
        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentContentDeltaEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentContentKind.User,
                "user-1",
                null,
                "First steer"));
        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentContentCompletedEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentContentKind.User,
                "user-1",
                null,
                "First steer"));

        Assert.AreEqual(1, tab.PendingSteers.Count);
        Assert.AreEqual("Second steer", tab.PendingSteers[0].Text);
    }

    [TestMethod]
    public void HandleAgentEvent_PublishesQueuedPromptListChangedWhenPendingSteerIsConsumed()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        tab.PendingSteers.Add(new PendingSteerPrompt("First steer"));
        var publisher = new FrontendEventPublisher(new InlineUiDispatcher());
        var events = new List<ShellFrontendEvent>();
        publisher.Subscribe(events.Add);

        var coordinator = CreateCoordinator(
            thread,
            tab,
            frontendEvents: publisher);
        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentContentCompletedEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentContentKind.User,
                "user-1",
                null,
                "First steer"));

        Assert.AreEqual(0, tab.PendingSteers.Count);
        Assert.AreEqual(1, events.OfType<QueuedPromptListChangedEvent>().Count(@event => @event.ThreadId == thread.ThreadId));
    }

    [TestMethod]
    public void HandleAgentEvent_DoesNotConsumePendingSteerDuringHistoryReplay()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        tab.HistoryLoading = true;
        tab.PendingSteers.Add(new PendingSteerPrompt("Pending steer"));

        var coordinator = CreateCoordinator(thread, tab);
        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentContentCompletedEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentContentKind.User,
                "user-1",
                null,
                "Pending steer"));

        Assert.AreEqual(1, tab.PendingSteers.Count);
    }

    [TestMethod]
    public void HandleAgentEvent_ClearsPendingSteersWhenSessionBecomesIdle()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        tab.PendingSteers.Add(new PendingSteerPrompt("Pending steer"));

        var coordinator = CreateCoordinator(thread, tab);
        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentSessionUpdateEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentSessionUpdateKind.Idle,
                "Idle"));

        Assert.AreEqual(0, tab.PendingSteers.Count);
    }

    [TestMethod]
    public void HandleAgentEvent_PublishesQueuedPromptListChangedWhenPendingSteersAreCleared()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        tab.PendingSteers.Add(new PendingSteerPrompt("Pending steer"));
        var publisher = new FrontendEventPublisher(new InlineUiDispatcher());
        var events = new List<ShellFrontendEvent>();
        publisher.Subscribe(events.Add);

        var coordinator = CreateCoordinator(
            thread,
            tab,
            frontendEvents: publisher);
        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentSessionUpdateEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentSessionUpdateKind.Idle,
                "Idle"));

        Assert.AreEqual(0, tab.PendingSteers.Count);
        Assert.AreEqual(1, events.OfType<QueuedPromptListChangedEvent>().Count(@event => @event.ThreadId == thread.ThreadId));
    }

    [TestMethod]
    public void HandleAgentEvent_TracksActiveRunIdAndClearsItWhenSessionBecomesIdle()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var coordinator = CreateCoordinator(thread, tab);

        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentContentDeltaEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                new AgentRunId("run-1"),
                AgentContentKind.Assistant,
                "assistant-1",
                null,
                "Working..."));

        Assert.AreEqual("run-1", tab.ActiveRunId?.Value);

        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentSessionUpdateEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                new AgentRunId("run-1"),
                AgentSessionUpdateKind.Idle,
                "Idle"));

        Assert.IsNull(tab.ActiveRunId);
    }

    [TestMethod]
    public void HandleAgentEvent_InvalidatesProjectFileSearchWhenFileChangesArrive()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var searchService = new FakeProjectFileSearchService();
        var coordinator = CreateCoordinator(thread, tab, projectFileSearchService: searchService);

        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentActivityEvent(
                AgentBackendIds.Codex,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentActivityKind.FileChange,
                AgentActivityPhase.Completed,
                "activity-1",
                null,
                "write_file",
                "Updated Program.cs"));

        Assert.AreEqual(1, searchService.Invalidations.Count);
        Assert.AreEqual(thread.WorkingDirectory, searchService.Invalidations[0].ProjectRoot);
        Assert.AreEqual(ProjectFileInvalidationReason.FileSystemWrite, searchService.Invalidations[0].Reason);
    }

    [TestMethod]
    public void ApplyRuntimeEvent_PublishesTypedProjectionEvents()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var dispatcher = new InlineUiDispatcher();
        var publisher = new FrontendEventPublisher(dispatcher);
        var events = new List<ShellFrontendEvent>();
        publisher.Subscribe(events.Add);
        var coordinator = CreateCoordinator(thread, tab, frontendEvents: publisher);

        coordinator.ApplyRuntimeEvent(new WorkThreadAgentEvent(
            thread.ThreadId,
            new AgentContentCompletedEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentContentKind.Assistant,
                "assistant-1",
                null,
                "Done")));

        Assert.IsTrue(events.OfType<RuntimeTimelineChangedEvent>().Any(@event => @event.ThreadId == thread.ThreadId));
        Assert.IsTrue(events.OfType<ShellChromeChangedEvent>().Any());
    }

    [TestMethod]
    public void ApplyRuntimeEvent_QueueEventAddsTimelineNoticeAndProjectionEvents()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var publisher = new FrontendEventPublisher(new InlineUiDispatcher());
        var events = new List<ShellFrontendEvent>();
        publisher.Subscribe(events.Add);
        var coordinator = CreateCoordinator(thread, tab, frontendEvents: publisher);

        coordinator.ApplyRuntimeEvent(new WorkThreadQueueRuntimeEvent(
            thread.ThreadId,
            DateTimeOffset.UtcNow,
            QueuedPromptCount: 1,
            QueueItemId: "queue-1",
            PromptPreview: "queued prompt",
            IsEnqueued: true));

        Assert.AreEqual(1, tab.Timeline.Flow.Items.Count);
        Assert.IsTrue(events.OfType<RuntimeTimelineChangedEvent>().Any(@event => @event.ThreadId == thread.ThreadId));
        Assert.IsTrue(events.OfType<ShellChromeChangedEvent>().Any());
    }

    [TestMethod]
    public void HandleAgentEvent_PublishesSessionUsageChangedWhenSelectedUsageChanges()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var publisher = new FrontendEventPublisher(new InlineUiDispatcher());
        var events = new List<ShellFrontendEvent>();
        publisher.Subscribe(events.Add);
        var coordinator = CreateCoordinator(thread, tab, frontendEvents: publisher);

        coordinator.HandleAgentEvent(
            thread,
            tab,
            new AgentSessionUpdateEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentSessionUpdateKind.UsageUpdated,
                "usage",
                Usage: new AgentSessionUsage(
                    Window: new AgentWindowUsageSnapshot(1200, 8000, 3, "window"),
                    Scope: AgentUsageScope.CurrentWindow,
                    Source: AgentUsageSource.CopilotSessionUsageInfo,
                    UpdatedAt: DateTimeOffset.UtcNow)));

        Assert.IsTrue(events.OfType<SessionUsageChangedEvent>().Any(@event => @event.ThreadId == thread.ThreadId));
    }

    [TestMethod]
    public void ApplyRuntimeEvent_PublishesPromptFocusRequestAfterShellChromeWhenSelectedThreadBecomesIdle()
    {
        var thread = CreateThread();
        var tab = CreateOpenThreadState(thread);
        var publisher = new FrontendEventPublisher(new InlineUiDispatcher());
        var events = new List<ShellFrontendEvent>();
        publisher.Subscribe(events.Add);
        var coordinator = CreateCoordinator(thread, tab, frontendEvents: publisher);

        coordinator.ApplyRuntimeEvent(
            new WorkThreadAgentEvent(
                thread.ThreadId,
                new AgentSessionUpdateEvent(
                    AgentBackendIds.Copilot,
                    "session-1",
                    DateTimeOffset.UtcNow,
                    null,
                    AgentSessionUpdateKind.Idle,
                    "idle")));

        var shellChromeIndex = events.FindIndex(static @event => @event is ShellChromeChangedEvent);
        var promptFocusIndex = events.FindIndex(static @event => @event is PromptFocusRequestedEvent);

        Assert.IsTrue(shellChromeIndex >= 0, "The idle runtime update should refresh shell chrome before focus is restored.");
        Assert.IsTrue(promptFocusIndex > shellChromeIndex, "Prompt focus should be requested after shell chrome refreshes so later projection updates do not steal focus.");
    }

    private static ThreadRuntimeEventCoordinator CreateCoordinator(
        WorkThreadDescriptor thread,
        OpenThreadState tab,
        IProjectFileSearchService? projectFileSearchService = null,
        IPluginAgentEventObserver? pluginAgentEventObserver = null,
        FrontendEventPublisher? frontendEvents = null)
    {
        var stateStore = new ShellStateStore(new InlineUiDispatcher());
        stateStore.Mutate(snapshot => snapshot.SetCatalog([], [thread]));
        return new ThreadRuntimeEventCoordinator(
            stateStore: stateStore,
            findOpenThread: id => id == tab.Thread.ThreadId ? tab : null,
            getAutoApproveEnabled: static () => false,
            isSelectedThread: id => id == thread.ThreadId,
            statusPort: new TestShellStatusPort(),
            drainQueuedPromptAsync: static (_, _) => Task.CompletedTask,
            projectFileSearchService: projectFileSearchService ?? NullProjectFileSearchService.Instance,
            pluginAgentEventObserver: pluginAgentEventObserver,
            frontendEvents: frontendEvents);
    }

    private sealed class RecordingPluginAgentEventObserver : IPluginAgentEventObserver
    {
        public WorkThreadDescriptor? ObservedThread { get; private set; }

        public AgentEvent? ObservedEvent { get; private set; }

        public IReadOnlyList<AgentEvent>? ProjectedEvents { get; private set; }

        public bool ProjectedIsReplay { get; private set; }

        private TaskCompletionSource _projectionCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ObserveAgentEventAsync(WorkThreadDescriptor thread, AgentEvent agentEvent, CancellationToken cancellationToken = default)
        {
            ObservedThread = thread;
            ObservedEvent = agentEvent;
            return Task.CompletedTask;
        }

        public Task<WorkThreadPluginDerivedEventProjectionResult> ProjectThreadEventsAsync(
            WorkThreadDescriptor thread,
            OpenThreadState tab,
            IReadOnlyList<AgentEvent> events,
            bool isReplay,
            CancellationToken cancellationToken = default)
        {
            ProjectedEvents = events;
            ProjectedIsReplay = isReplay;
            _projectionCompletion.TrySetResult();
            return Task.FromResult(new WorkThreadPluginDerivedEventProjectionResult([], []));
        }

        public Task WaitForProjectionAsync()
            => _projectionCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class TestShellStatusPort : IShellStatusPort
    {
        public void SetShellStatus(ShellStatusUpdate update)
        {
        }

        public void SetThreadStatus(OpenThreadState thread, ThreadStatusUpdate update)
        {
            thread.StatusMessage = update.Message;
            thread.StatusBusy = update.ShowSpinner;
            thread.StatusTone = update.Tone;
            thread.HasCustomStatus = true;
        }

        public void ClearThreadStatus(OpenThreadState thread)
        {
            thread.StatusMessage = null;
            thread.StatusBusy = false;
            thread.StatusTone = StatusTone.Info;
            thread.HasCustomStatus = false;
        }

        public void SetProviderSessionLoadStatus(string? message)
        {
        }
    }

    private static OpenThreadState CreateOpenThreadState(WorkThreadDescriptor thread)
    {
        var timeline = new ThreadTimelinePresenter(new InlineUiDispatcher(), static () => null);
        return new OpenThreadState(thread, timeline);
    }

    private static WorkThreadDescriptor CreateThread()
    {
        return new WorkThreadDescriptor
        {
            ThreadId = "thread-1",
            Kind = WorkThreadKind.ProjectThread,
            BackendId = AgentBackendIds.Copilot.Value,
            BackendSessionId = "backend-thread-1",
            ProjectRef = "project-1",
            WorkingDirectory = @"C:\code\CodeAlta",
            Title = "Review startup",
            Status = WorkThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow
        };
    }

    private static AgentSystemPromptEvent CreateSystemPromptEvent(
        DateTimeOffset timestamp,
        string hash,
        string systemMessage,
        string developerInstructions,
        string changeKind)
        => new(
            AgentBackendIds.Copilot,
            "session-1",
            timestamp,
            null,
            "session_start",
            hash,
            systemMessage,
            developerInstructions,
            new AgentSystemPromptProviderPayloadSummary("native-system-and-developer", true, false),
            null,
            new AgentSystemPromptStatistics(1, 1, 2, 6, 9),
            new AgentSystemPromptChangeSummary(changeKind, ["base/default"], [], []));

    private static VStack GetChatCardStack(DocumentFlowItem item)
    {
        var document = Assert.IsInstanceOfType<FlowDocument>(item.Content);
        Assert.AreEqual(1, document.BlockCount);
        var block = Assert.IsInstanceOfType<VisualDocumentFlowBlock>(document.GetBlock(0));
        var group = Assert.IsInstanceOfType<Group>(block.CreateVisual());
        return Assert.IsInstanceOfType<VStack>(group.Content);
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess()
            => true;

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
        }

        public Task InvokeAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return Task.FromResult(action());
        }
    }

    private sealed class FakeProjectFileSearchService : IProjectFileSearchService
    {
        public List<(string ProjectRoot, ProjectFileInvalidationReason Reason)> Invalidations { get; } = [];

        public ValueTask<IProjectFileSearchSession> CreateSessionAsync(
            ProjectFileSearchSessionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<ProjectFileResolution> ResolveAsync(
            ProjectFileResolveQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask RecordUsageAsync(
            ProjectFileUsageEvent usageEvent,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask InvalidateAsync(
            string projectRoot,
            ProjectFileInvalidationReason reason,
            CancellationToken cancellationToken = default)
        {
            Invalidations.Add((projectRoot, reason));
            return ValueTask.CompletedTask;
        }
    }
}
