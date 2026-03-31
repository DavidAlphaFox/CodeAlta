using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellThreadStateCoordinatorTests
{
    [TestMethod]
    public void ApplyRecoveredCatalogState_AppliesPersistedThreadLocalState()
    {
        using var temp = TempDirectory.Create();
        var options = new CatalogOptions { GlobalRoot = temp.Path };
        var coordinator = CreateCoordinator(options);
        coordinator.ViewState = new WorkThreadViewState
        {
            ThreadStates = new Dictionary<string, WorkThreadLocalState>(StringComparer.OrdinalIgnoreCase)
            {
                ["thread-1"] = new WorkThreadLocalState
                {
                    Archived = true,
                    MessageCount = 12,
                },
            },
        };

        coordinator.ApplyRecoveredCatalogState([], [CreateThread("thread-1")]);

        var thread = coordinator.Threads.Single();
        Assert.AreEqual(WorkThreadStatus.Archived, thread.Status);
        Assert.AreEqual(12, thread.MessageCount);
    }

    [TestMethod]
    public async Task PersistThreadLocalStateAsync_StoresArchivedAndMessageCountInViewState()
    {
        using var temp = TempDirectory.Create();
        var options = new CatalogOptions { GlobalRoot = temp.Path };
        var threadCatalog = new WorkThreadCatalog(options);
        var coordinator = CreateCoordinator(options, threadCatalog);
        coordinator.ViewState = new WorkThreadViewState();

        var thread = CreateThread("thread-1");
        thread.Status = WorkThreadStatus.Archived;
        thread.MessageCount = 6;
        await coordinator.PersistThreadLocalStateAsync(thread).ConfigureAwait(false);

        var reloaded = await threadCatalog.LoadViewStateAsync().ConfigureAwait(false);
        Assert.IsTrue(reloaded.ThreadStates["thread-1"].Archived);
        Assert.AreEqual(6, reloaded.ThreadStates["thread-1"].MessageCount);
    }

    [TestMethod]
    public async Task CloseThreadAsync_LastSelectedProjectThreadFallsBackToProjectScope()
    {
        using var temp = TempDirectory.Create();
        var options = new CatalogOptions { GlobalRoot = temp.Path };
        var coordinator = CreateCoordinator(options);
        var project = CreateProject("project-1", "CodeAlta");
        coordinator.ApplyRecoveredCatalogState([project], [CreateThread("thread-1", project.Id)]);
        coordinator.OpenThread("thread-1");

        await coordinator.CloseThreadAsync("thread-1").ConfigureAwait(false);

        Assert.IsTrue(coordinator.DraftTabOpen);
        Assert.IsFalse(coordinator.GlobalScopeSelected);
        Assert.AreEqual(project.Id, coordinator.SelectedProjectId);
        Assert.IsNull(coordinator.SelectedThreadId);
        Assert.AreEqual(ShellSurface.DraftWorkspace, coordinator.Selection.Surface);
        Assert.IsInstanceOfType<WorkspaceTarget.Draft>(coordinator.Selection.Target);
    }

    [TestMethod]
    public async Task CloseThreadAsync_RetainsThreadStateForReopen()
    {
        using var temp = TempDirectory.Create();
        var options = new CatalogOptions { GlobalRoot = temp.Path };
        var coordinator = CreateCoordinator(options);
        var project = CreateProject("project-1", "CodeAlta");
        var thread = CreateThread("thread-1", project.Id);
        coordinator.ApplyRecoveredCatalogState([project], [thread]);
        coordinator.OpenThread(thread.ThreadId);

        var tab = coordinator.FindOpenThread(thread.ThreadId);
        Assert.IsNotNull(tab);
        tab.Session.PromptDraftText = "keep this draft";

        await coordinator.CloseThreadAsync(thread.ThreadId).ConfigureAwait(false);

        var retained = coordinator.FindOpenThread(thread.ThreadId);
        Assert.IsNotNull(retained);
        Assert.AreEqual("keep this draft", retained.Session.PromptDraftText);
        Assert.IsFalse(coordinator.ViewState.OpenThreadIds.Contains(thread.ThreadId, StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void EnsureThreadTab_LoadsPersistedPromptDraft()
    {
        using var temp = TempDirectory.Create();
        var options = new CatalogOptions { GlobalRoot = temp.Path };
        var coordinator = CreateCoordinator(options, loadPromptDraft: static threadId => threadId == "thread-1" ? "saved prompt" : null);
        var project = CreateProject("project-1", "CodeAlta");
        var thread = CreateThread("thread-1", project.Id);
        coordinator.ApplyRecoveredCatalogState([project], [thread]);

        var tab = coordinator.EnsureThreadTab(thread);

        Assert.AreEqual("saved prompt", tab.Session.PromptDraftText);
    }

    [TestMethod]
    public void RemoveDeletedProject_SelectedProjectScopeFallsBackToGlobal()
    {
        using var temp = TempDirectory.Create();
        var options = new CatalogOptions { GlobalRoot = temp.Path };
        var coordinator = CreateCoordinator(options);
        var project = CreateProject("project-1", "CodeAlta");
        coordinator.ApplyRecoveredCatalogState([project], [CreateThread("thread-1", project.Id)]);
        coordinator.SelectProjectScope(project.Id);

        coordinator.RemoveDeletedProject(project.Id, ["thread-1"]);

        Assert.IsTrue(coordinator.DraftTabOpen);
        Assert.IsTrue(coordinator.GlobalScopeSelected);
        Assert.IsNull(coordinator.SelectedThreadId);
        Assert.AreEqual(ShellSurface.DraftWorkspace, coordinator.Selection.Surface);
        Assert.IsInstanceOfType<WorkspaceTarget.Draft>(coordinator.Selection.Target);
    }

    [TestMethod]
    public async Task SaveNavigatorSettingsAsync_PersistsUpdatedSettings()
    {
        using var temp = TempDirectory.Create();
        var options = new CatalogOptions { GlobalRoot = temp.Path };
        var threadCatalog = new WorkThreadCatalog(options);
        var coordinator = CreateCoordinator(options, threadCatalog);

        await coordinator.SaveNavigatorSettingsAsync(new NavigatorSettings
        {
            SortMode = NavigatorProjectSortMode.Date,
            RecentThreadsPerProject = 7,
        }).ConfigureAwait(false);

        var viewState = await threadCatalog.LoadViewStateAsync().ConfigureAwait(false);
        Assert.AreEqual(NavigatorProjectSortMode.Date, viewState.Navigator.SortMode);
        Assert.AreEqual(7, viewState.Navigator.RecentThreadsPerProject);
    }

    private static ShellThreadStateCoordinator CreateCoordinator(
        CatalogOptions options,
        WorkThreadCatalog? threadCatalog = null,
        Func<string, string?>? loadPromptDraft = null)
    {
        threadCatalog ??= new WorkThreadCatalog(options);
        return new ShellThreadStateCoordinator(
            new ProjectCatalog(options),
            threadCatalog,
            static () => new InlineUiDispatcher(),
            static () => null,
            static _ => true,
            loadPromptDraft ?? (static _ => null),
            static _ => { },
            static _ => { },
            static (_, _, _, _, _) => { },
            static (_, _) => Task.CompletedTask,
            static () => { },
            static () => { },
            static () => { },
            static _ => { },
            static (_, _, _) => { });
    }

    private static WorkThreadDescriptor CreateThread(string threadId)
        => CreateThread(threadId, "project-1");

    private static WorkThreadDescriptor CreateThread(string threadId, string projectId)
    {
        var timestamp = DateTimeOffset.Parse("2026-03-29T12:00:00+00:00");
        return new WorkThreadDescriptor
        {
            ThreadId = threadId,
            Kind = WorkThreadKind.ProjectThread,
            BackendId = AgentBackendIds.Codex.Value,
            BackendSessionId = $"session-{threadId}",
            ProjectRef = "project-1",
            WorkingDirectory = @"C:\repo",
            Title = "Test thread",
            Status = WorkThreadStatus.Active,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            LastActiveAt = timestamp,
        };
    }

    private static ProjectDescriptor CreateProject(string id, string displayName)
    {
        return new ProjectDescriptor
        {
            Id = id,
            Slug = displayName.ToLowerInvariant(),
            Name = displayName,
            DisplayName = displayName,
            ProjectPath = $@"C:\repo\{displayName}",
            DefaultBranch = "main",
        };
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

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

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CodeAlta.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
