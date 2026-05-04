using CodeAlta.Threading;
using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using System.IO;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaShellControllerTests
{
    [TestMethod]
    public async Task ReloadCatalogAsync_UpdatesStatusAndAppliesRecoveredState()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var importer = new FakeImporter(log);
        var project = new ProjectDescriptor { Id = "project-1", DisplayName = "CodeAlta", ProjectPath = @"C:\repo", Slug = "codealta" };
        var projectCatalog = new FakeProjectCatalogStore(log, [project]);
        var threads = new FakeRecoverableThreadSource(log, [CreateThread("thread-1")]);
        var dispatcher = new FakeUiDispatcher();
        var controller = new CodeAltaShellController(shell, importer, projectCatalog, threads, new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(dispatcher);

        await controller.ReloadCatalogAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "Shell.Status:Refreshing project and thread catalog...:True:Info",
                "Importer.Import",
                "ProjectCatalog.Load",
                "ThreadSource.List",
                "Shell.ApplyRecoveredCatalogState:1:1",
                "Shell.SetReadyStatus",
            },
            log);
        Assert.AreEqual(2, dispatcher.InvokeCallCount);
    }

    [TestMethod]
    public async Task ReloadCatalogAsync_FailureSetsErrorStatus()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var importer = new FakeImporter(log) { ImportException = new InvalidOperationException("boom") };
        var dispatcher = new FakeUiDispatcher();
        var controller = new CodeAltaShellController(
            shell,
            importer,
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, []),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(dispatcher);

        await controller.ReloadCatalogAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "Shell.Status:Refreshing project and thread catalog...:True:Info",
                "Importer.Import",
                "Shell.Status:Failed to refresh catalog: boom:False:Error",
            },
            log);
    }

    [TestMethod]
    public async Task InitializeAsync_RefreshesCatalogAndSchedulesStartupRestore()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, [new ProjectDescriptor { Id = "project-1", DisplayName = "CodeAlta", ProjectPath = @"C:\repo", Slug = "codealta" }]),
            new FakeRecoverableThreadSource(log, [CreateThread("thread-1")]),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(new FakeUiDispatcher());

        await controller.InitializeAsync(CancellationToken.None);

        CollectionAssert.Contains(log, "Shell.InitializeChatBackends");
        CollectionAssert.Contains(log, "Shell.RefreshCatalogAndThreadWorkspace");
        CollectionAssert.Contains(log, "Shell.SetReadyStatus");
        CollectionAssert.Contains(log, "Shell.SetInitialized:True");
        CollectionAssert.Contains(log, "Importer.Import");
        CollectionAssert.Contains(log, "ProjectCatalog.Load");
        CollectionAssert.Contains(log, "ThreadSource.List");
        CollectionAssert.Contains(log, "Shell.ApplyRecoveredCatalogState:1:1");
        CollectionAssert.Contains(log, "Shell.TrySchedulePendingStartupThreadRestore");
    }

    [TestMethod]
    public async Task InitializeAsync_MarksShellInitializedBeforeBackendInitializationCompletes()
    {
        var log = new List<string>();
        var shell = new FakeShell(log)
        {
            InitializeChatBackendsCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, [new ProjectDescriptor { Id = "project-1", DisplayName = "CodeAlta", ProjectPath = @"C:\repo", Slug = "codealta" }]),
            new FakeRecoverableThreadSource(log, [CreateThread("thread-1")]),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(new FakeUiDispatcher());

        var initializationTask = controller.InitializeAsync(CancellationToken.None);

        await WaitUntilAsync(() => log.Contains("Shell.SetInitialized:True")).ConfigureAwait(false);

        Assert.IsFalse(initializationTask.IsCompleted, "Initialization should continue in the background while the shell is usable.");
        Assert.IsFalse(log.Contains("Importer.Import"), "Session catalog loading should wait until provider initialization finishes.");

        shell.InitializeChatBackendsCompletion.SetResult(true);
        await initializationTask.ConfigureAwait(false);

        Assert.IsTrue(log.IndexOf("Shell.SetInitialized:True") < log.IndexOf("Importer.Import"));
    }

    [TestMethod]
    public async Task InitializeAsync_AppliesRecoverableThreadsAsProviderProgressCompletes()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var codexBackendId = new AgentBackendId("codex");
        var slowBackendId = new AgentBackendId("slow");
        var importer = new FakeProgressImporter(log, codexBackendId, slowBackendId);
        var project = new ProjectDescriptor { Id = "project-1", DisplayName = "CodeAlta", ProjectPath = @"C:\repo", Slug = "codealta" };
        var threadSource = new FakeRecoverableThreadSource(
            log,
            [
                CreateThread("thread-codex", backendId: codexBackendId.Value),
                CreateThread("thread-slow", backendId: slowBackendId.Value),
            ]);
        var controller = new CodeAltaShellController(
            shell,
            importer,
            new FakeProjectCatalogStore(log, [project]),
            threadSource,
            new FakeWorkThreadDeleter(log),
            [
                new AgentBackendDescriptor(codexBackendId, "Codex"),
                new AgentBackendDescriptor(slowBackendId, "Slow"),
            ]);
        controller.AttachUiDispatcher(new FakeUiDispatcher());

        var initializationTask = controller.InitializeAsync(CancellationToken.None);

        await WaitUntilAsync(() => importer.FirstProgressReported.Task.IsCompleted).ConfigureAwait(false);
        await WaitUntilAsync(() => log.Contains("Shell.ApplyRecoveredCatalogState:1:1:KeepMissing")).ConfigureAwait(false);

        Assert.IsFalse(initializationTask.IsCompleted, "Initialization should not wait for every provider before applying the first recovered batch.");
        CollectionAssert.Contains(log, "ThreadSource.List:codex");
        Assert.IsFalse(log.Contains("ThreadSource.List:slow"), "The slow provider should not be queried before it reports completion.");

        importer.AllowCompletion();
        await initializationTask.ConfigureAwait(false);

        CollectionAssert.Contains(log, "Shell.ApplyRecoveredCatalogState:1:2:KeepMissing");
        CollectionAssert.Contains(log, "Shell.ApplyRecoveredCatalogState:1:2");
    }

    [TestMethod]
    public async Task InitializeAsync_LoadsReadyProviderSessionsWhileAnotherProviderIsInitializing()
    {
        var log = new List<string>();
        var codexBackendId = new AgentBackendId("codex");
        var slowBackendId = new AgentBackendId("slow");
        var shell = new FakeShell(log);
        shell.BlockBackendInitialization(slowBackendId);
        var importer = new FakeProgressImporter(log, codexBackendId, slowBackendId, blockSecondImport: false);
        var project = new ProjectDescriptor { Id = "project-1", DisplayName = "CodeAlta", ProjectPath = @"C:\repo", Slug = "codealta" };
        var threadSource = new FakeRecoverableThreadSource(
            log,
            [
                CreateThread("thread-codex", backendId: codexBackendId.Value),
                CreateThread("thread-slow", backendId: slowBackendId.Value),
            ]);
        var controller = new CodeAltaShellController(
            shell,
            importer,
            new FakeProjectCatalogStore(log, [project]),
            threadSource,
            new FakeWorkThreadDeleter(log),
            [
                new AgentBackendDescriptor(codexBackendId, "Codex"),
                new AgentBackendDescriptor(slowBackendId, "Slow"),
            ]);
        controller.AttachUiDispatcher(new FakeUiDispatcher());

        var initializationTask = controller.InitializeAsync(CancellationToken.None);

        await WaitUntilAsync(() => log.Contains("ThreadSource.List:codex")).ConfigureAwait(false);

        Assert.IsFalse(initializationTask.IsCompleted, "The slow provider should still be initializing.");
        CollectionAssert.Contains(log, "ProgressImporter.ImportBackend:codex");
        Assert.IsFalse(log.Contains("ProgressImporter.ImportBackend:slow"), "The slow provider should not load sessions before initialization completes.");
        Assert.IsFalse(log.Contains("ThreadSource.List:slow"), "The slow provider should not be queried before initialization completes.");

        shell.CompleteBackendInitialization(slowBackendId);
        await initializationTask.ConfigureAwait(false);

        CollectionAssert.Contains(log, "ThreadSource.List:slow");
    }

    [TestMethod]
    public async Task ApplyRuntimeEventAsync_RoutesEventThroughDispatcher()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var dispatcher = new FakeUiDispatcher();
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, []),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(dispatcher);
        var runtimeEvent = CreateHostEvent("thread-1");

        await controller.ApplyRuntimeEventAsync(runtimeEvent, CancellationToken.None);

        Assert.AreSame(runtimeEvent, shell.LastRuntimeEvent);
        Assert.AreEqual(1, dispatcher.InvokeCallCount);
        CollectionAssert.AreEqual(
            new[]
            {
                "Shell.HandleRuntimeEvent:thread-1",
            },
            log);
    }

    [TestMethod]
    public void FormatProviderSessionLoadStatus_ShowsProgressAndProviderNames()
    {
        var status = CodeAltaShellController.FormatProviderSessionLoadStatus(
            new ProviderSessionLoadProgress(
                AgentBackendIds.Codex,
                "Codex",
                1,
                2,
                ["Copilot"]));

        Assert.AreEqual("Loading Copilot sessions [■■■■□□□□] 1/2", status);
    }

    [TestMethod]
    public void FormatProviderSessionLoadStatus_HidesWhenComplete()
    {
        var status = CodeAltaShellController.FormatProviderSessionLoadStatus(
            new ProviderSessionLoadProgress(
                AgentBackendIds.Codex,
                "Codex",
                2,
                2,
                []));

        Assert.IsNull(status);
    }

    [TestMethod]
    public void FormatStartupProviderLoadStatus_ShowsCombinedProviderProgress()
    {
        var status = CodeAltaShellController.FormatStartupProviderLoadStatus(
            new ProviderSessionLoadProgress(
                AgentBackendIds.Codex,
                "Codex",
                1,
                3,
                ["OpenAI", "Gemma", "Anthropic"]));

        Assert.AreEqual("Loading OpenAI, Gemma, … [■■■□□□□□] 1/3", status);
    }

    [TestMethod]
    public void QueueRuntimeEvent_AutoDrainAppliesQueuedEventsWhenDispatcherIsAttached()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var dispatcher = new FakeUiDispatcher();
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, []),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(dispatcher);

        controller.QueueRuntimeEvent(CreateHostEvent("thread-1"), CancellationToken.None);
        controller.QueueRuntimeEvent(CreateHostEvent("thread-2"), CancellationToken.None);
        Assert.AreEqual(0, dispatcher.InvokeCallCount);
        CollectionAssert.AreEqual(
            new[]
            {
                "Shell.HandleRuntimeEvent:thread-1",
                "Shell.HandleRuntimeEvent:thread-2",
            },
            log);
    }

    [TestMethod]
    public void DrainPendingRuntimeEvents_MergesCompatibleContentDeltas()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, []),
            new FakeWorkThreadDeleter(log));

        controller.QueueRuntimeEvent(CreateToolDeltaEvent("thread-1", "tool-1", "alpha"), CancellationToken.None);
        controller.QueueRuntimeEvent(CreateToolDeltaEvent("thread-1", "tool-1", "beta"), CancellationToken.None);

        var drained = controller.DrainPendingRuntimeEvents();

        Assert.AreEqual(2, drained);
        Assert.AreEqual(1, log.Count);
        var runtimeEvent = Assert.IsInstanceOfType<WorkThreadAgentEvent>(shell.LastRuntimeEvent);
        var delta = Assert.IsInstanceOfType<AgentContentDeltaEvent>(runtimeEvent.Event);
        Assert.AreEqual("alphabeta", delta.Delta);
    }

    [TestMethod]
    public async Task SelectGlobalScopeAsync_RoutesSelectionThroughDispatcher()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var dispatcher = new FakeUiDispatcher();
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, []),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(dispatcher);

        await controller.SelectGlobalScopeAsync(CancellationToken.None);

        Assert.AreEqual(1, dispatcher.InvokeCallCount);
        CollectionAssert.AreEqual(new[] { "Shell.SelectGlobalScope" }, log);
    }

    [TestMethod]
    public async Task SelectProjectScopeAsync_RoutesProjectScopeThroughDispatcher()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var dispatcher = new FakeUiDispatcher();
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, []),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(dispatcher);

        await controller.SelectProjectScopeAsync("project-1", CancellationToken.None);

        Assert.AreEqual(1, dispatcher.InvokeCallCount);
        CollectionAssert.AreEqual(new[] { "Shell.SelectProjectScope:project-1" }, log);
    }

    [TestMethod]
    public async Task OpenThreadAsync_RoutesThreadSelectionThroughDispatcher()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var dispatcher = new FakeUiDispatcher();
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, []),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(dispatcher);

        await controller.OpenThreadAsync("thread-1", CancellationToken.None);

        Assert.AreEqual(1, dispatcher.InvokeCallCount);
        CollectionAssert.AreEqual(new[] { "Shell.OpenThread:thread-1", "Shell.FocusPromptEditor" }, log);
    }

    [TestMethod]
    public async Task OpenFolderAsync_UpsertsProjectAndSelectsProjectWithoutReloadingThreads()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var catalog = new FakeProjectCatalogStore(log, []);
        var dispatcher = new FakeUiDispatcher();
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            catalog,
            new FakeRecoverableThreadSource(log, [CreateThread("thread-1")]),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(dispatcher);

        var projectPath = Path.Combine(Path.GetTempPath(), "codealta-open-folder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectPath);

        try
        {
            var project = await controller.OpenFolderAsync(projectPath, CancellationToken.None);

            Assert.AreEqual(projectPath, project.ProjectPath);
            CollectionAssert.AreEqual(
                new[]
                {
                    $"Shell.Status:Opening '{projectPath}'...:True:Info",
                    $"ProjectCatalog.UpsertFromPath:{projectPath}",
                    $"Shell.UpsertProject:{project.Id}",
                    $"Shell.SelectProjectScope:{project.Id}",
                    "Shell.SetReadyStatus",
                    "Shell.FocusPromptEditor",
                },
                log);
            Assert.AreEqual(2, dispatcher.InvokeCallCount);
        }
        finally
        {
            Directory.Delete(projectPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task OpenFolderAsync_ProjectReference_SelectsExistingProjectAndFocusesPrompt()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var existingProject = new ProjectDescriptor
        {
            Id = "project-1",
            Slug = "codealta",
            Name = "CodeAlta",
            DisplayName = "CodeAlta",
            ProjectPath = @"C:\repo",
            DefaultBranch = "main",
        };
        var catalog = new FakeProjectCatalogStore(log, [existingProject]);
        var dispatcher = new FakeUiDispatcher();
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            catalog,
            new FakeRecoverableThreadSource(log, [CreateThread("thread-1")]),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(dispatcher);

        var project = await controller.OpenFolderAsync("CodeAlta", CancellationToken.None);

        Assert.AreSame(existingProject, project);
        CollectionAssert.AreEqual(
            new[]
            {
                "Shell.Status:Opening 'CodeAlta'...:True:Info",
                "ProjectCatalog.Load",
                "Shell.UpsertProject:project-1",
                "Shell.SelectProjectScope:project-1",
                "Shell.SetReadyStatus",
                "Shell.FocusPromptEditor",
            },
            log);
    }

    [TestMethod]
    public async Task OpenFolderAsync_HiddenProjectReference_RemainsHiddenUnlessIncluded()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var hiddenProject = new ProjectDescriptor
        {
            Id = "project-1",
            Slug = "codealta",
            Name = "CodeAlta",
            DisplayName = "CodeAlta",
            ProjectPath = @"C:\repo",
            DefaultBranch = "main",
            Archived = true,
        };
        var catalog = new FakeProjectCatalogStore(log, [hiddenProject]);
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            catalog,
            new FakeRecoverableThreadSource(log, [CreateThread("thread-1")]),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(new FakeUiDispatcher());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => controller.OpenFolderAsync("CodeAlta", includeHidden: false, CancellationToken.None));
        Assert.IsTrue(hiddenProject.Archived);

        log.Clear();
        var reopenedProject = await controller.OpenFolderAsync("CodeAlta", includeHidden: true, CancellationToken.None);

        Assert.AreSame(hiddenProject, reopenedProject);
        Assert.IsFalse(hiddenProject.Archived);
        CollectionAssert.Contains(log, "ProjectCatalog.Save:project-1");
        CollectionAssert.Contains(log, "Shell.FocusPromptEditor");
    }

    [TestMethod]
    public async Task OpenFolderAsync_TildePath_ExpandsHomeDirectoryBeforeUpsert()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.IsFalse(string.IsNullOrWhiteSpace(homeDirectory));

        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, [CreateThread("thread-1")]),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(new FakeUiDispatcher());

        var tempProject = Path.Combine(homeDirectory, "codealta-open-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempProject);

        try
        {
            var relativeHomePath = "~" + tempProject[homeDirectory.Length..];
            var project = await controller.OpenFolderAsync(relativeHomePath, CancellationToken.None);

            Assert.AreEqual(tempProject, project.ProjectPath);
            CollectionAssert.Contains(log, $"ProjectCatalog.UpsertFromPath:{tempProject}");
            CollectionAssert.Contains(log, "Shell.FocusPromptEditor");
        }
        finally
        {
            Directory.Delete(tempProject, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadProjectThreadsAsync_ReturnsProjectThreadsOrderedByRecency()
    {
        var log = new List<string>();
        var threads = new[]
        {
            CreateThread("thread-1"),
            CreateThread("thread-2"),
            CreateThread("thread-3", projectId: "project-2"),
        };
        threads[0].LastActiveAt = DateTimeOffset.Parse("2026-03-29T10:00:00+00:00");
        threads[1].LastActiveAt = DateTimeOffset.Parse("2026-03-29T11:00:00+00:00");
        var controller = new CodeAltaShellController(
            new FakeShell(log),
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, threads),
            new FakeWorkThreadDeleter(log));

        var projectThreads = await controller.LoadProjectThreadsAsync("project-1", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "thread-2", "thread-1" }, projectThreads.Select(static thread => thread.ThreadId).ToArray());
    }

    [TestMethod]
    public async Task SaveProjectAsync_PersistsAndReloadsCatalog()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var project = new ProjectDescriptor { Id = "project-1", DisplayName = "CodeAlta", ProjectPath = @"C:\repo", Slug = "codealta" };
        var catalog = new FakeProjectCatalogStore(log, [project]);
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            catalog,
            new FakeRecoverableThreadSource(log, [CreateThread("thread-1")]),
            new FakeWorkThreadDeleter(log));
        controller.AttachUiDispatcher(new FakeUiDispatcher());

        project.DisplayName = "CodeAlta UI";
        await controller.SaveProjectAsync(project, CancellationToken.None);

        Assert.AreEqual("CodeAlta UI", catalog.Projects.Single().DisplayName);
        CollectionAssert.Contains(log, "ProjectCatalog.Save:project-1");
        CollectionAssert.Contains(log, "Shell.ApplyRecoveredCatalogState:1:1");
    }

    [TestMethod]
    public async Task DeleteThreadAsync_DeletesThreadAndReloadsCatalog()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var thread = CreateThread("thread-1");
        var deleter = new FakeWorkThreadDeleter(log) { DeleteResult = true };
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            new FakeProjectCatalogStore(log, []),
            new FakeRecoverableThreadSource(log, [thread]),
            deleter);
        controller.AttachUiDispatcher(new FakeUiDispatcher());

        var deletedByBackend = await controller.DeleteThreadAsync(thread.ThreadId, CancellationToken.None);

        Assert.IsTrue(deletedByBackend);
        Assert.AreEqual(thread.ThreadId, deleter.DeletedThreadIds.Single());
        CollectionAssert.Contains(log, "Shell.ApplyRecoveredCatalogState:0:1");
    }

    [TestMethod]
    public async Task DeleteProjectAsync_DeletesThreadsMarksProjectArchivedAndReloadsCatalog()
    {
        var log = new List<string>();
        var shell = new FakeShell(log);
        var project = new ProjectDescriptor { Id = "project-1", DisplayName = "CodeAlta", ProjectPath = @"C:\repo", Slug = "codealta" };
        var catalog = new FakeProjectCatalogStore(log, [project]);
        var deleter = new FakeWorkThreadDeleter(log);
        var controller = new CodeAltaShellController(
            shell,
            new FakeImporter(log),
            catalog,
            new FakeRecoverableThreadSource(log, [CreateThread("thread-1"), CreateThread("thread-2")]),
            deleter);
        controller.AttachUiDispatcher(new FakeUiDispatcher());

        var result = await controller.DeleteProjectAsync(project.Id, CancellationToken.None);

        Assert.IsTrue(catalog.Projects.Single().Archived);
        CollectionAssert.AreEquivalent(new[] { "thread-1", "thread-2" }, deleter.DeletedThreadIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { "thread-1", "thread-2" }, result.DeletedThreadIds.ToArray());
        CollectionAssert.Contains(log, "ProjectCatalog.Save:project-1");
    }

    private static WorkThreadHostEvent CreateHostEvent(string threadId)
        => new(threadId, DateTimeOffset.UtcNow, AgentSessionUpdateKind.Info, "Updated");

    private static WorkThreadAgentEvent CreateToolDeltaEvent(string threadId, string contentId, string delta)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return new WorkThreadAgentEvent(
            threadId,
            new AgentContentDeltaEvent(
                AgentBackendIds.Codex,
                "session-1",
                timestamp,
                null,
                AgentContentKind.ToolOutput,
                contentId,
                "activity-1",
                delta));
    }

    private static WorkThreadDescriptor CreateThread(
        string threadId,
        string projectId = "project-1",
        string? backendId = null)
    {
        return new WorkThreadDescriptor
        {
            ThreadId = threadId,
            Kind = WorkThreadKind.ProjectThread,
            BackendId = backendId ?? AgentBackendIds.Codex.Value,
            BackendSessionId = $"session-{threadId}",
            ProjectRef = projectId,
            WorkingDirectory = @"C:\repo",
            Title = "Test thread",
            Status = WorkThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail("The expected condition was not reached before the timeout elapsed.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private sealed class FakeShell(List<string> log) : ICodeAltaShell
    {
        private readonly Dictionary<string, TaskCompletionSource<bool>> _backendInitializationCompletions = new(StringComparer.OrdinalIgnoreCase);

        public WorkThreadRuntimeEvent? LastRuntimeEvent { get; private set; }

        public TaskCompletionSource<bool>? InitializeChatBackendsCompletion { get; init; }

        public Task InitializeChatBackendsAsync(CancellationToken cancellationToken)
        {
            log.Add("Shell.InitializeChatBackends");
            return InitializeChatBackendsCompletion?.Task ?? Task.CompletedTask;
        }

        public Task InitializeChatBackendAsync(AgentBackendId backendId, CancellationToken cancellationToken)
        {
            log.Add($"Shell.InitializeChatBackend:{backendId.Value}");
            return _backendInitializationCompletions.TryGetValue(backendId.Value, out var completion)
                ? completion.Task.WaitAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public void BlockBackendInitialization(AgentBackendId backendId)
            => _backendInitializationCompletions[backendId.Value] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void CompleteBackendInitialization(AgentBackendId backendId)
            => _backendInitializationCompletions[backendId.Value].TrySetResult(true);

        public void SetStatus(string message, bool showSpinner = false, StatusTone tone = StatusTone.Info)
            => log.Add($"Shell.Status:{message}:{showSpinner}:{tone}");

        public void SetProviderSessionLoadStatus(string? message)
        {
        }

        public void ApplyRecoveredCatalogState(
            IReadOnlyList<ProjectDescriptor> projects,
            IReadOnlyList<WorkThreadDescriptor> threads,
            bool pruneMissingThreads = true)
            => log.Add($"Shell.ApplyRecoveredCatalogState:{projects.Count}:{threads.Count}" + (pruneMissingThreads ? string.Empty : ":KeepMissing"));

        public void UpsertProject(ProjectDescriptor project)
            => log.Add($"Shell.UpsertProject:{project.Id}");

        public void SetReadyStatusForCurrentSelection()
            => log.Add("Shell.SetReadyStatus");

        public void HandleRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent)
        {
            LastRuntimeEvent = runtimeEvent;
            log.Add($"Shell.HandleRuntimeEvent:{runtimeEvent.ThreadId}");
        }

        public void RefreshCatalogAndThreadWorkspace()
            => log.Add("Shell.RefreshCatalogAndThreadWorkspace");

        public void TrySchedulePendingStartupThreadRestore(CancellationToken cancellationToken)
            => log.Add("Shell.TrySchedulePendingStartupThreadRestore");

        public void SelectGlobalScope()
            => log.Add("Shell.SelectGlobalScope");

        public void SelectProjectScope(string projectId)
            => log.Add($"Shell.SelectProjectScope:{projectId}");

        public void OpenThread(string threadId)
            => log.Add($"Shell.OpenThread:{threadId}");

        public void FocusPromptEditor()
            => log.Add("Shell.FocusPromptEditor");

        public void SetInitialized(bool isInitialized)
            => log.Add($"Shell.SetInitialized:{isInitialized}");
    }

    private sealed class FakeImporter(List<string> log) : IKnownProjectImporter
    {
        public Exception? ImportException { get; set; }

        public Task ImportAsync(CancellationToken cancellationToken)
        {
            log.Add("Importer.Import");
            if (ImportException is not null)
            {
                throw ImportException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeProgressImporter(
        List<string> log,
        AgentBackendId firstBackendId,
        AgentBackendId secondBackendId,
        bool blockSecondImport = true) : IKnownProjectImporterWithProgress
    {
        private readonly TaskCompletionSource<bool> _allowCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> FirstProgressReported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ImportAsync(CancellationToken cancellationToken)
            => ImportAsync(static _ => { }, cancellationToken);

        public async Task ImportAsync(Action<ProviderSessionLoadProgress> reportProgress, CancellationToken cancellationToken)
        {
            log.Add("ProgressImporter.Import.Start");
            reportProgress(new ProviderSessionLoadProgress(firstBackendId, "Codex", 1, 2, ["Slow"]));
            FirstProgressReported.TrySetResult(true);

            await _allowCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            reportProgress(new ProviderSessionLoadProgress(secondBackendId, "Slow", 2, 2, []));
            log.Add("ProgressImporter.Import.End");
        }

        public void AllowCompletion()
            => _allowCompletion.TrySetResult(true);

        public async Task ImportBackendAsync(AgentBackendDescriptor descriptor, CancellationToken cancellationToken)
        {
            log.Add($"ProgressImporter.ImportBackend:{descriptor.BackendId.Value}");
            if (descriptor.BackendId == firstBackendId)
            {
                FirstProgressReported.TrySetResult(true);
                return;
            }

            if (blockSecondImport && descriptor.BackendId == secondBackendId)
            {
                await _allowCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class FakeProjectCatalogStore(List<string> log, IReadOnlyList<ProjectDescriptor> projects) : IProjectCatalogStore
    {
        public List<ProjectDescriptor> Projects { get; } = projects.ToList();

        public Task<IReadOnlyList<ProjectDescriptor>> LoadAsync(CancellationToken cancellationToken)
        {
            log.Add("ProjectCatalog.Load");
            return Task.FromResult<IReadOnlyList<ProjectDescriptor>>(Projects.ToArray());
        }

        public Task<ProjectDescriptor?> GetByIdAsync(string projectId, CancellationToken cancellationToken)
        {
            log.Add($"ProjectCatalog.GetById:{projectId}");
            return Task.FromResult(Projects.FirstOrDefault(project => string.Equals(project.Id, projectId, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<ProjectDescriptor> UpsertFromPathAsync(string projectPath, CancellationToken cancellationToken)
        {
            log.Add($"ProjectCatalog.UpsertFromPath:{projectPath}");
            var existing = Projects.FirstOrDefault(project =>
                string.Equals(project.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Archived = false;
                return Task.FromResult(existing);
            }

            var created = new ProjectDescriptor
            {
                Id = $"project-{Projects.Count + 1}",
                Slug = $"project-{Projects.Count + 1}",
                Name = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                DisplayName = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                ProjectPath = projectPath,
                DefaultBranch = "main",
            };
            Projects.Add(created);
            return Task.FromResult(created);
        }

        public Task SaveAsync(ProjectDescriptor project, CancellationToken cancellationToken)
        {
            log.Add($"ProjectCatalog.Save:{project.Id}");
            Projects.RemoveAll(existing => string.Equals(existing.Id, project.Id, StringComparison.OrdinalIgnoreCase));
            Projects.Add(project);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRecoverableThreadSource(List<string> log, IReadOnlyList<WorkThreadDescriptor> threads) : IRecoverableThreadSource
    {
        public Task<IReadOnlyList<WorkThreadDescriptor>> ListRecoverableThreadsAsync(CancellationToken cancellationToken)
        {
            log.Add("ThreadSource.List");
            return Task.FromResult(threads);
        }

        public Task<IReadOnlyList<WorkThreadDescriptor>> ListRecoverableThreadsAsync(
            Func<AgentBackendId, bool>? shouldListBackendSessions,
            CancellationToken cancellationToken)
        {
            var filteredThreads = threads
                .Where(thread => shouldListBackendSessions?.Invoke(new AgentBackendId(thread.BackendId)) != false)
                .ToArray();
            var backendIds = filteredThreads
                .Select(static thread => thread.BackendId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static backendId => backendId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            log.Add(backendIds.Length == 0
                ? "ThreadSource.List:empty"
                : $"ThreadSource.List:{string.Join(",", backendIds)}");
            return Task.FromResult<IReadOnlyList<WorkThreadDescriptor>>(filteredThreads);
        }
    }

    private sealed class FakeWorkThreadDeleter(List<string> log) : IWorkThreadDeleter
    {
        public List<string> DeletedThreadIds { get; } = [];

        public bool DeleteResult { get; set; }

        public Task<bool> DeleteThreadAsync(WorkThreadDescriptor thread, CancellationToken cancellationToken)
        {
            log.Add($"ThreadDeleter.Delete:{thread.ThreadId}");
            DeletedThreadIds.Add(thread.ThreadId);
            thread.Status = WorkThreadStatus.Archived;
            return Task.FromResult(DeleteResult);
        }
    }

    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public int InvokeCallCount { get; private set; }

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
            InvokeCallCount++;
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeCallCount++;
            return Task.FromResult(action());
        }
    }
}
