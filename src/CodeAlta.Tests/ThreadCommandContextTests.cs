using CodeAlta.App;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Prompting;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ThreadCommandContextTests
{
    [TestMethod]
    public void ClearDraftInput_InvokesCallbackOnUiDispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        var calledOnUiThread = false;
        var context = CreateContext(
            dispatcher,
            clearDraftInput: () => calledOnUiThread = dispatcher.CheckAccess());

        context.ClearDraftInput();

        Assert.IsTrue(calledOnUiThread);
        Assert.AreEqual(1, dispatcher.InvokeActionCount);
    }

    [TestMethod]
    public void IsThreadInputEmpty_InvokesCallbackOnUiDispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        var callbackRanOnUi = false;
        var context = CreateContext(
            dispatcher,
            isThreadInputEmpty: () =>
            {
                callbackRanOnUi = dispatcher.CheckAccess();
                return true;
            });

        var isEmpty = context.IsThreadInputEmpty();

        Assert.IsTrue(isEmpty);
        Assert.IsTrue(callbackRanOnUi);
        Assert.AreEqual(1, dispatcher.InvokeFuncCount);
    }

    [TestMethod]
    public void CaptureThreadInput_IncludesClonedPromptImages()
    {
        var dispatcher = new RecordingUiDispatcher();
        var image = PromptImageAttachment.Create("Image-1", [1, 2, 3], "image/png", ".png");
        var context = CreateContext(
            dispatcher,
            snapshotPromptImages: () => [image]);

        var submission = context.CaptureThreadInput(string.Empty);

        Assert.AreEqual(string.Empty, submission.Text);
        Assert.AreEqual(1, submission.Images.Count);
        Assert.AreEqual("Image-1", submission.Images[0].Title);
        Assert.AreNotSame(image.Bytes, submission.Images[0].Bytes);
        image.Bytes[0] = 9;
        Assert.AreEqual(1, submission.Images[0].Bytes[0]);
    }

    [TestMethod]
    public void RestoreThreadInput_RestoresTextAndPromptImages()
    {
        var dispatcher = new RecordingUiDispatcher();
        var restoredText = string.Empty;
        IReadOnlyList<PromptImageAttachment>? restoredImages = null;
        var context = CreateContext(
            dispatcher,
            restoreThreadInput: text => restoredText = text,
            restorePromptImages: images => restoredImages = images);
        var image = PromptImageAttachment.Create("Image-1", [1, 2, 3], "image/png", ".png");

        context.RestoreThreadInput(PromptSubmission.Create("retry", [image]));

        Assert.AreEqual("retry", restoredText);
        Assert.IsNotNull(restoredImages);
        Assert.AreEqual(1, restoredImages.Count);
        Assert.AreEqual("Image-1", restoredImages[0].Title);
    }

    [TestMethod]
    public void SetShellStatus_InvokesStatusCallbackOnUiDispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        var callbackRanOnUi = false;
        var context = CreateContext(
            dispatcher,
            setShellStatus: (_, _, _) => callbackRanOnUi = dispatcher.CheckAccess());

        context.SetShellStatus("Ready", false, StatusTone.Ready);

        Assert.IsTrue(callbackRanOnUi);
        Assert.AreEqual(1, dispatcher.InvokeActionCount);
    }

    private static ThreadCommandContext CreateContext(
        IUiDispatcher dispatcher,
        Action? clearDraftInput = null,
        Func<bool>? isThreadInputEmpty = null,
        Action<string>? restoreThreadInput = null,
        Func<IReadOnlyList<PromptImageAttachment>>? snapshotPromptImages = null,
        Action<IReadOnlyList<PromptImageAttachment>>? restorePromptImages = null,
        Action<string, bool, StatusTone>? setShellStatus = null)
    {
        clearDraftInput ??= static () => { };
        isThreadInputEmpty ??= static () => true;
        restoreThreadInput ??= static _ => { };
        snapshotPromptImages ??= static () => [];
        restorePromptImages ??= static _ => { };
        setShellStatus ??= static (_, _, _) => { };
        var scheduler = new FrontendUiScheduler(dispatcher);
        var promptSessionId = new PromptSessionId("prompt-1");
        var promptSessionPort = new PromptSessionPort(
            scheduler,
            isThreadInputEmpty,
            static () => { },
            restoreThreadInput,
            snapshotPromptImages,
            restorePromptImages);
        promptSessionPort.BindPromptSession(new PromptSessionBinding(
            promptSessionId,
            ProjectId.NewVersion7(),
            new ShellThreadRef.Draft(new ThreadDraftId("draft-1")),
            new ModelProviderId("provider-1")));

        return new ThreadCommandContext(
            new DelegatingThreadLifecycleCommandPort(
                static _ => Task.FromResult<WorkThreadDescriptor?>(null),
                static _ => Task.FromResult<WorkThreadDescriptor?>(null),
                static () => Task.CompletedTask),
            new ThreadCommandUiPort(
                scheduler,
                static () => false,
                static () => true,
                clearDraftInput,
                static () => { },
                static () => { },
                static () => { },
                static (_, _, _) => { }),
            promptSessionPort,
            () => promptSessionId,
            new ShellStatusPort(scheduler, setShellStatus, static (_, _, _, _) => { }));
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        private int _depth;

        public int InvokeActionCount { get; private set; }

        public int InvokeFuncCount { get; private set; }

        public bool CheckAccess() => _depth > 0;

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _depth++;
            try
            {
                action();
            }
            finally
            {
                _depth--;
            }
        }

        public Task InvokeAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeActionCount++;
            _depth++;
            try
            {
                action();
                return Task.CompletedTask;
            }
            finally
            {
                _depth--;
            }
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeFuncCount++;
            _depth++;
            try
            {
                return Task.FromResult(action());
            }
            finally
            {
                _depth--;
            }
        }
    }
}
