using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellSelectionPortTests
{
    [TestMethod]
    public void GetSnapshot_CapturesSelectionProjectThreadAndPromptSession()
    {
        var project = new ProjectDescriptor { Id = "project-1", DisplayName = "CodeAlta", ProjectPath = @"C:\repo", Slug = "codealta" };
        var thread = new WorkThreadDescriptor { ThreadId = "thread-1", ProjectRef = project.Id, Title = "Thread" };
        var promptSession = new PromptSessionBinding(
            new PromptSessionId("prompt-1"),
            ProjectId.NewVersion7(),
            new ShellThreadRef.Running(thread.ThreadId),
            new ModelProviderId("provider-1"));
        var selection = ShellSelection.Thread(thread.ThreadId, project.Id);
        var port = new DelegatingShellSelectionPort(
            () => selection,
            () => project,
            () => thread,
            () => promptSession,
            (_, _) => Task.CompletedTask,
            id => id == thread.ThreadId);

        var snapshot = port.GetSnapshot();

        Assert.AreEqual(selection, snapshot.Selection);
        Assert.AreSame(project, snapshot.SelectedProject);
        Assert.AreSame(thread, snapshot.SelectedThread);
        Assert.AreSame(promptSession, snapshot.PromptSession);
        Assert.IsTrue(port.IsSelectedThread(thread.ThreadId));
    }

    [TestMethod]
    public async Task SelectAsync_ForwardsSelectionAndCancellationToken()
    {
        var capturedSelection = (ShellSelection?)null;
        var capturedToken = CancellationToken.None;
        using var cancellation = new CancellationTokenSource();
        var port = new DelegatingShellSelectionPort(
            () => ShellSelection.GlobalDraft(),
            () => null,
            () => null,
            () => null,
            (selection, token) =>
            {
                capturedSelection = selection;
                capturedToken = token;
                return Task.CompletedTask;
            },
            _ => false);
        var requested = ShellSelection.ProjectDraft("project-1");

        await port.SelectAsync(requested, cancellation.Token);

        Assert.AreEqual(requested, capturedSelection);
        Assert.AreEqual(cancellation.Token, capturedToken);
    }

    [TestMethod]
    public void IsSelectedThread_RejectsBlankThreadIdBeforeForwarding()
    {
        var invoked = false;
        var port = new DelegatingShellSelectionPort(
            () => ShellSelection.GlobalDraft(),
            () => null,
            () => null,
            () => null,
            (_, _) => Task.CompletedTask,
            _ =>
            {
                invoked = true;
                return true;
            });

        Assert.ThrowsExactly<ArgumentException>(() => port.IsSelectedThread("   "));
        Assert.IsFalse(invoked);
    }
}
