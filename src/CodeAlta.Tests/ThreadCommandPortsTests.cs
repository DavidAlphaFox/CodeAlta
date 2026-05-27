using CodeAlta.App;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Timeline;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ThreadCommandPortsTests
{
    [TestMethod]
    public async Task LifecyclePort_ForwardsCreationPersistenceAndRekey()
    {
        var created = new SessionViewDescriptor { ThreadId = "thread-1", ProviderId = "codex", ProviderKey = "codex" };
        var persisted = false;
        (string OldThreadId, SessionViewDescriptor Thread)? rekey = null;
        var port = new DelegatingThreadLifecycleCommandPort(
            title =>
            {
                Assert.AreEqual("hello", title);
                return Task.FromResult<SessionViewDescriptor?>(created);
            },
            static _ => Task.FromResult<SessionViewDescriptor?>(null),
            () =>
            {
                persisted = true;
                return Task.CompletedTask;
            },
            (oldThreadId, thread) => rekey = (oldThreadId, thread));

        var result = await port.CreateGlobalThreadAsync("hello");
        await port.PersistViewStateAsync();
        port.RekeyThreadIdentity("old-thread", created);

        Assert.AreSame(created, result);
        Assert.IsTrue(persisted);
        Assert.IsNotNull(rekey);
        Assert.AreEqual("old-thread", rekey.Value.OldThreadId);
        Assert.AreSame(created, rekey.Value.Thread);
    }

    [TestMethod]
    public void UiPort_MarshalsCallbacksThroughDispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        var cleared = false;
        var rendered = false;
        var port = new ThreadCommandUiPort(
            dispatcher,
            static () => true,
            static () => false,
            () => cleared = true,
            static () => { },
            static () => { },
            static () => { },
            (_, action, _) =>
            {
                rendered = true;
                action();
            });
        var actionRan = false;

        Assert.IsTrue(port.TrySetPromptUnavailableStatus());
        Assert.IsFalse(port.GetAutoApproveEnabled());
        port.ClearDraftInput();
        port.TryRenderInteraction(CreateThreadState(), () => actionRan = true, "test");

        Assert.IsTrue(cleared);
        Assert.IsTrue(rendered);
        Assert.IsTrue(actionRan);
        Assert.AreEqual(4, dispatcher.InvokeCount);
    }

    private static OpenThreadState CreateThreadState()
    {
        var thread = new SessionViewDescriptor { ThreadId = "thread-1", ProviderId = "codex", ProviderKey = "codex" };
        return new OpenThreadState(thread, new ThreadTimelinePresenter(new ImmediateUiDispatcher(), static () => null));
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public int InvokeCount { get; private set; }

        public bool CheckAccess() => true;

        public void VerifyAccess()
        {
        }

        public void Post(Action action)
            => action();

        public void PostDeferred(Action action)
            => action();

        public void Invoke(Action action)
        {
            InvokeCount++;
            action();
        }

        public T Invoke<T>(Func<T> action)
        {
            InvokeCount++;
            return action();
        }

        public Task InvokeAsync(Action action)
        {
            Invoke(action);
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
            => Task.FromResult(Invoke(action));
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action)
            => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
            => Task.FromResult(action());
    }
}
