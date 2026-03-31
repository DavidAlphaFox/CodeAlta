using CodeAlta.Agent;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Timeline;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellSelectionModelTests
{
    [TestMethod]
    public void ShellSelection_GlobalDraft_CanRetainPreferredProject()
    {
        var selection = ShellSelection.GlobalDraft("project-1");

        Assert.IsTrue(selection.DraftTabOpen);
        Assert.IsTrue(selection.GlobalScopeSelected);
        Assert.AreEqual("project-1", selection.SelectedProjectId);
        Assert.IsNull(selection.SelectedThreadId);
    }

    [TestMethod]
    public void OpenThreadState_SplitsSessionWorkspaceAndTimelineState()
    {
        var thread = new WorkThreadDescriptor
        {
            ThreadId = "thread-1",
            Kind = WorkThreadKind.ProjectThread,
            BackendId = AgentBackendIds.Codex.Value,
            BackendSessionId = "session-1",
            ProjectRef = "project-1",
            WorkingDirectory = @"C:\code\CodeAlta",
            Title = "Investigate shell selection",
            Status = WorkThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        };
        var timeline = new ThreadTimelinePresenter(new InlineUiDispatcher(), () => true, static () => null);

        var state = new OpenThreadState(thread, timeline);

        Assert.AreSame(thread, state.Thread);
        Assert.IsNotNull(state.Session);
        Assert.AreSame(state.Workspace.ViewModel, state.ViewModel);
        Assert.AreSame(state.TimelineState.Presenter, state.Timeline);
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
}
