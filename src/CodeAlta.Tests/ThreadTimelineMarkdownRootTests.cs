using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Presentation.Timeline;
using CodeAlta.Threading;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ThreadTimelineMarkdownRootTests
{
    [TestMethod]
    public void CreatePendingChatMessage_AssignsLocalFileRootPathToUserAndAssistantMarkdown()
    {
        const string projectRoot = @"C:\code\CodeAlta";

        var pending = ChatTimelineVisualFactory.CreatePendingChatMessage("Open [readme](readme.md).", projectRoot);

        Assert.AreEqual(projectRoot, pending.StreamingMarkdown.Options.LocalFileRootPath);
        Assert.AreEqual(projectRoot, GetMarkdownControl(pending.UserItem).Options.LocalFileRootPath);
    }

    [TestMethod]
    public void ThreadTimelinePresenter_SetLocalFileRootPath_UpdatesExistingAndFutureMarkdownControls()
    {
        const string initialProjectRoot = @"C:\code\CodeAlta";
        const string updatedProjectRoot = @"D:\code\CodeAlta";
        var timeline = new ThreadTimelinePresenter(new InlineUiDispatcher(), static () => null, initialProjectRoot);

        timeline.FinalizeContent(new AgentContentCompletedEvent(
            AgentBackendIds.Codex,
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            AgentContentKind.User,
            "user-1",
            null,
            "Open [readme](readme.md)."));

        var firstMarkdown = GetMarkdownControl(timeline.Flow.Items[0]);
        Assert.AreEqual(initialProjectRoot, firstMarkdown.Options.LocalFileRootPath);

        timeline.SetLocalFileRootPath(updatedProjectRoot);

        Assert.AreEqual(updatedProjectRoot, firstMarkdown.Options.LocalFileRootPath);

        timeline.FinalizeContent(new AgentContentCompletedEvent(
            AgentBackendIds.Codex,
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            AgentContentKind.Assistant,
            "assistant-1",
            null,
            "Open [guide](doc/development-guide.md)."));

        var secondMarkdown = GetMarkdownControl(timeline.Flow.Items[^1]);
        Assert.AreEqual(updatedProjectRoot, secondMarkdown.Options.LocalFileRootPath);
    }

    [TestMethod]
    public void OpenThreadRegistry_RefreshesTimelineLocalFileRootPathFromProjectMapping()
    {
        var project = new ProjectDescriptor
        {
            Id = "project-1",
            DisplayName = "CodeAlta",
            ProjectPath = @"C:\code\CodeAlta",
        };
        var registry = new OpenThreadRegistry(
            () => new InlineUiDispatcher(),
            static () => null,
            static _ => null,
            static _ => { },
            static (_, _, _, _) => { },
            () => project,
            projectId => string.Equals(projectId, project.Id, StringComparison.OrdinalIgnoreCase) ? project : null);
        var thread = CreateThread();

        var firstTab = registry.EnsureThreadTab(thread);

        Assert.AreEqual(project.ProjectPath, firstTab.Timeline.LocalFileRootPath);

        project.ProjectPath = @"D:\code\CodeAlta";

        var refreshedTab = registry.EnsureThreadTab(thread);

        Assert.AreSame(firstTab, refreshedTab);
        Assert.AreEqual(project.ProjectPath, refreshedTab.Timeline.LocalFileRootPath);
    }

    private static MarkdownControl GetMarkdownControl(DocumentFlowItem item)
    {
        Assert.IsInstanceOfType<FlowDocument>(item.Content);
        var document = (FlowDocument)item.Content;
        Assert.AreEqual(1, document.BlockCount);
        Assert.IsInstanceOfType<VisualDocumentFlowBlock>(document.GetBlock(0));
        var group = Assert.IsInstanceOfType<Group>(((VisualDocumentFlowBlock)document.GetBlock(0)).CreateVisual());
        return Assert.IsInstanceOfType<MarkdownControl>(group.Content);
    }

    private static WorkThreadDescriptor CreateThread()
    {
        return new WorkThreadDescriptor
        {
            ThreadId = "thread-1",
            Kind = WorkThreadKind.ProjectThread,
            BackendId = AgentBackendIds.Codex.Value,
            BackendSessionId = "backend-thread-1",
            ProjectRef = "project-1",
            WorkingDirectory = @"C:\code\CodeAlta",
            Title = "Review startup",
            Status = WorkThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        };
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
}
