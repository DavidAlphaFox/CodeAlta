using CodeAlta.App;
using CodeAlta.Models;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellStatusPortTests
{
    [TestMethod]
    public void SetShellStatus_InvokesCallbackThroughScheduler()
    {
        var scheduler = new RecordingScheduler();
        var captured = default(ShellStatusUpdate);
        var port = new ShellStatusPort(
            scheduler,
            (message, showSpinner, tone) => captured = new ShellStatusUpdate(message, showSpinner, tone),
            static (_, _, _, _) => { });

        port.SetShellStatus(new ShellStatusUpdate("Working", true, StatusTone.Info));

        Assert.AreEqual(new ShellStatusUpdate("Working", true, StatusTone.Info), captured);
        Assert.AreEqual(1, scheduler.InvokeCount);
    }

    [TestMethod]
    public void SetProviderSessionLoadStatus_AllowsNullMessage()
    {
        var scheduler = new RecordingScheduler();
        string? captured = "unchanged";
        var port = new ShellStatusPort(
            scheduler,
            static (_, _, _) => { },
            static (_, _, _, _) => { },
            setProviderSessionLoadStatus: message => captured = message);

        port.SetProviderSessionLoadStatus(null);

        Assert.IsNull(captured);
        Assert.AreEqual(1, scheduler.InvokeCount);
    }

    [TestMethod]
    public void SetShellStatus_RejectsEmptyMessage()
    {
        var port = new ShellStatusPort(
            new RecordingScheduler(),
            static (_, _, _) => { },
            static (_, _, _, _) => { });

        Assert.ThrowsExactly<ArgumentException>(() => port.SetShellStatus(new ShellStatusUpdate(string.Empty, false, StatusTone.Info)));
    }

    private sealed class RecordingScheduler : IFrontendUiScheduler
    {
        public IUiDispatcher Dispatcher { get; } = new InlineUiDispatcher();

        public int InvokeCount { get; private set; }

        public void VerifyAccess()
        {
        }

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
        }

        public void PostDeferred(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
        }

        public void Invoke(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeCount++;
            action();
        }

        public T Invoke<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeCount++;
            return action();
        }
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
