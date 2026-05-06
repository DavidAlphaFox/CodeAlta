using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class FrontendUiSchedulerTests
{
    [TestMethod]
    public void Invoke_RunsInlineWhenDispatcherHasAccess()
    {
        var dispatcher = new RecordingUiDispatcher(hasAccess: true);
        var scheduler = new FrontendUiScheduler(dispatcher);
        var invoked = false;

        scheduler.Invoke(() => invoked = true);
        var value = scheduler.Invoke(static () => 42);

        Assert.IsTrue(invoked);
        Assert.AreEqual(42, value);
        Assert.AreEqual(0, dispatcher.InvokeCount);
    }

    [TestMethod]
    public void Invoke_MarshalsThroughDispatcherWhenAccessIsMissing()
    {
        var dispatcher = new RecordingUiDispatcher(hasAccess: false);
        var scheduler = new FrontendUiScheduler(dispatcher);
        var invoked = false;

        scheduler.Invoke(() => invoked = true);
        var value = scheduler.Invoke(static () => 42);

        Assert.IsTrue(invoked);
        Assert.AreEqual(42, value);
        Assert.AreEqual(2, dispatcher.InvokeCount);
    }

    [TestMethod]
    public void PostDeferred_AlwaysPostsInsteadOfRunningInline()
    {
        var dispatcher = new RecordingUiDispatcher(hasAccess: true);
        var scheduler = new FrontendUiScheduler(dispatcher);
        var invoked = false;

        scheduler.PostDeferred(() => invoked = true);

        Assert.IsFalse(invoked);
        Assert.AreEqual(1, dispatcher.Posted.Count);

        dispatcher.DrainPosted();
        Assert.IsTrue(invoked);
    }

    [TestMethod]
    public void VerifyAccess_ThrowsWhenDispatcherAccessIsMissing()
    {
        var scheduler = new FrontendUiScheduler(new RecordingUiDispatcher(hasAccess: false));

        Assert.ThrowsExactly<InvalidOperationException>(scheduler.VerifyAccess);
    }

    [TestMethod]
    public void DispatcherProvider_UsesLatestDispatcherForUiOperations()
    {
        var staleDispatcher = new RecordingUiDispatcher(hasAccess: false);
        var activeDispatcher = new RecordingUiDispatcher(hasAccess: true);
        var currentDispatcher = staleDispatcher;
        var scheduler = new FrontendUiScheduler(() => currentDispatcher);
        currentDispatcher = activeDispatcher;
        var invoked = false;

        scheduler.Post(() => invoked = true);

        Assert.IsTrue(invoked);
        Assert.AreSame(activeDispatcher, scheduler.Dispatcher);
        Assert.AreEqual(0, staleDispatcher.Posted.Count);
        Assert.AreEqual(0, staleDispatcher.InvokeCount);
    }

    private sealed class RecordingUiDispatcher(bool hasAccess) : IUiDispatcher
    {
        public List<Action> Posted { get; } = [];

        public int InvokeCount { get; private set; }

        public bool CheckAccess()
            => hasAccess;

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            Posted.Add(action);
        }

        public Task InvokeAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeCount++;
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeCount++;
            return Task.FromResult(action());
        }

        public void DrainPosted()
        {
            foreach (var action in Posted.ToArray())
            {
                action();
            }

            Posted.Clear();
        }
    }
}
