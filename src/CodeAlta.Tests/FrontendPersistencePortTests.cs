using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.Tests;

[TestClass]
public sealed class FrontendPersistencePortTests
{
    [TestMethod]
    public void PromptDraftOperations_UsePromptSessionIdAsPersistenceKey()
    {
        string? loadedKey = null;
        string? deletedKey = null;
        var port = new FrontendPersistencePort(
            key =>
            {
                loadedKey = key;
                return "draft text";
            },
            key => deletedKey = key,
            static _ => Task.CompletedTask,
            static (_, _) => Task.CompletedTask);
        var promptSessionId = new PromptSessionId("prompt-1");

        var draft = port.LoadPromptDraft(promptSessionId);
        port.DeletePromptDraft(promptSessionId);

        Assert.AreEqual("draft text", draft);
        Assert.AreEqual("prompt-1", loadedKey);
        Assert.AreEqual("prompt-1", deletedKey);
    }

    [TestMethod]
    public async Task AsyncOperations_ForwardCancellationTokenAndThread()
    {
        var cancellation = new CancellationTokenSource();
        var persistToken = default(CancellationToken);
        var registerToken = default(CancellationToken);
        WorkThreadDescriptor? registeredThread = null;
        var port = new FrontendPersistencePort(
            static _ => null,
            static _ => { },
            token =>
            {
                persistToken = token;
                return Task.CompletedTask;
            },
            (thread, token) =>
            {
                registeredThread = thread;
                registerToken = token;
                return Task.CompletedTask;
            });
        var descriptor = new WorkThreadDescriptor { ThreadId = "thread-1", Title = "Thread" };

        await port.PersistViewStateAsync(cancellation.Token);
        await port.RegisterCreatedThreadAsync(descriptor, cancellation.Token);

        Assert.AreEqual(cancellation.Token, persistToken);
        Assert.AreEqual(cancellation.Token, registerToken);
        Assert.AreSame(descriptor, registeredThread);
    }

    [TestMethod]
    public void LoadPromptDraft_RejectsDefaultPromptSessionId()
    {
        var port = new FrontendPersistencePort(
            static _ => null,
            static _ => { },
            static _ => Task.CompletedTask,
            static (_, _) => Task.CompletedTask);

        Assert.ThrowsExactly<ArgumentException>(() => port.LoadPromptDraft(default));
    }
}
