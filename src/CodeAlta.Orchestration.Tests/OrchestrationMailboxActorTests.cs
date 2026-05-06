using CodeAlta.Orchestration.Runtime.Actors;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class OrchestrationMailboxActorTests
{
    [TestMethod]
    public async Task PostAsync_ExecutesCommandsInMailboxOrder()
    {
        await using var actor = new OrchestrationMailboxActor(capacity: 4);
        var observed = new List<int>();

        await actor.PostAsync(_ =>
        {
            observed.Add(1);
            return ValueTask.CompletedTask;
        });
        await actor.PostAsync(_ =>
        {
            observed.Add(2);
            return ValueTask.CompletedTask;
        });
        await actor.StopAsync();

        CollectionAssert.AreEqual(new[] { 1, 2 }, observed);
    }

    [TestMethod]
    public async Task AskAsync_CompletesReplyAfterCommandRuns()
    {
        await using var actor = new OrchestrationMailboxActor(capacity: 2);

        var result = await actor.AskAsync(_ => ValueTask.FromResult("reply"));

        Assert.AreEqual("reply", result);
    }

    [TestMethod]
    public async Task PostAsync_WaitsWhenBoundedMailboxIsFull()
    {
        await using var actor = new OrchestrationMailboxActor(capacity: 1);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await actor.PostAsync(async _ => await releaseFirst.Task.ConfigureAwait(false));
        await actor.PostAsync(_ => ValueTask.CompletedTask);

        var thirdPost = actor.PostAsync(_ => ValueTask.CompletedTask).AsTask();
        await Task.Delay(50);
        Assert.IsFalse(thirdPost.IsCompleted);

        releaseFirst.SetResult();
        await thirdPost;
    }

    [TestMethod]
    public async Task ReservedPostAsync_UsesReservedMailboxCapacity()
    {
        await using var actor = new OrchestrationMailboxActor(capacity: 1);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await actor.PostAsync(async _ => await releaseFirst.Task.ConfigureAwait(false));
        await actor.PostAsync(_ => ValueTask.CompletedTask);

        var blockedNormalPost = actor.PostAsync(_ => ValueTask.CompletedTask).AsTask();
        await Task.Delay(50);
        Assert.IsFalse(blockedNormalPost.IsCompleted);

        var reservedPost = actor.PostReservedAsync(_ => ValueTask.CompletedTask).AsTask();
        await reservedPost.WaitAsync(TimeSpan.FromSeconds(5));

        releaseFirst.SetResult();
        await blockedNormalPost;
    }

    [TestMethod]
    public async Task StopAsync_CancelsRunningCommandAndRejectsLaterPosts()
    {
        var actor = new OrchestrationMailboxActor(capacity: 1);
        await actor.PostAsync(async cancellationToken => await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));

        await actor.StopAsync(cancelPending: true);

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await actor.PostAsync(_ => ValueTask.CompletedTask));
    }

    [TestMethod]
    public async Task Diagnostics_ReportFailuresAndStop()
    {
        await using var actor = new OrchestrationMailboxActor(capacity: 2);
        var diagnostics = new List<OrchestrationActorDiagnosticKind>();
        actor.DiagnosticEmitted += (_, diagnostic) => diagnostics.Add(diagnostic.Kind);

        await actor.PostAsync(_ => throw new InvalidOperationException("boom"));
        await actor.StopAsync();

        CollectionAssert.Contains(diagnostics, OrchestrationActorDiagnosticKind.CommandFailed);
        Assert.AreEqual(OrchestrationActorDiagnosticKind.Stopped, diagnostics[^1]);
    }
}
