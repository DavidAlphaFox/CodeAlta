using CodeAlta.App.State;
using CodeAlta.Threading;
using CodeAlta.ViewModels;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellFrontendStateStoreTests
{
    [TestMethod]
    public void Mutate_PublishesImmutableSnapshots()
    {
        var store = new ShellFrontendStateStore();
        var first = store.Snapshot;

        var updated = store.Mutate(snapshot => snapshot
            .UpsertTab(new ShellFrontendTabSnapshot("thread-1", "Thread", "thread"))
            .SetStatus("Ready"));

        Assert.AreEqual(0, first.Tabs.Count);
        Assert.AreEqual(1, updated.Tabs.Count);
        Assert.AreEqual("thread-1", updated.ActiveTabId);
        Assert.AreEqual("Ready", updated.StatusText);
        Assert.AreSame(updated, store.Snapshot);
    }

    [TestMethod]
    public void RemoveTab_SelectsRemainingTabWhenActiveTabIsRemoved()
    {
        var snapshot = ShellFrontendStateSnapshot.Empty
            .UpsertTab(new ShellFrontendTabSnapshot("thread-1", "Thread 1", "thread"))
            .UpsertTab(new ShellFrontendTabSnapshot("thread-2", "Thread 2", "thread"))
            .SelectTab("thread-2");

        var updated = snapshot.RemoveTab("thread-2");

        Assert.AreEqual(1, updated.Tabs.Count);
        Assert.AreEqual("thread-1", updated.ActiveTabId);
    }

    [TestMethod]
    public void SelectTab_RejectsMissingTab()
    {
        var snapshot = ShellFrontendStateSnapshot.Empty;

        Assert.ThrowsExactly<InvalidOperationException>(() => snapshot.SelectTab("missing"));
    }

    [TestMethod]
    public async Task Snapshot_RejectsAccessFromNonOwnerThread()
    {
        var store = new ShellFrontendStateStore();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Task.Run(() => _ = store.Snapshot));
    }

    [TestMethod]
    public void ChatSelectorStateStore_RejectsAccessOffUiThread()
    {
        var store = new ChatSelectorStateStore(
            new ThreadWorkspaceViewModel(),
            new FrontendUiScheduler(new NonOwnerUiDispatcher()));

        Assert.ThrowsExactly<InvalidOperationException>(store.VerifyBindableAccess);
    }

    private sealed class NonOwnerUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => false;

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
