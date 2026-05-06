using CodeAlta.App;
using CodeAlta.Models;
using CodeAlta.Presentation.Prompting;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class LegacyPromptSessionPortTests
{
    [TestMethod]
    public void CaptureAndRestorePrompt_UseUiSchedulerAndImages()
    {
        var scheduler = new RecordingScheduler();
        var image = PromptImageAttachment.Create("Image-1", [1, 2, 3], "image/png", ".png");
        var restoredText = string.Empty;
        IReadOnlyList<PromptImageAttachment>? restoredImages = null;
        var port = new LegacyPromptSessionPort(
            scheduler,
            static () => false,
            static () => { },
            text => restoredText = text,
            () => [image],
            images => restoredImages = images);
        var promptSessionId = new PromptSessionId("prompt-1");

        var submission = port.CapturePrompt(promptSessionId, "hello");
        port.RestorePrompt(promptSessionId, submission);

        Assert.AreEqual("hello", submission.Text);
        Assert.AreEqual(1, submission.Images.Count);
        Assert.AreEqual(2, scheduler.InvokeCount);
        Assert.AreEqual("hello", restoredText);
        Assert.IsNotNull(restoredImages);
        Assert.AreEqual(1, restoredImages.Count);
    }

    [TestMethod]
    public void PromptOperations_RejectDefaultPromptSessionId()
    {
        var port = new LegacyPromptSessionPort(
            new RecordingScheduler(),
            static () => false,
            static () => { },
            static _ => { },
            static () => [],
            static _ => { });

        Assert.ThrowsExactly<ArgumentException>(() => port.CapturePrompt(default, "hello"));
        Assert.ThrowsExactly<ArgumentException>(() => port.ClearPrompt(default));
        Assert.ThrowsExactly<ArgumentException>(() => port.IsPromptEmpty(default));
    }

    private sealed class RecordingScheduler : IFrontendUiScheduler
    {
        public int InvokeCount { get; private set; }

        public IUiDispatcher Dispatcher { get; } = new ImmediateUiDispatcher();

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
