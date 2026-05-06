using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class WorkThreadOrchestratorContractsTests
{
    [TestMethod]
    public void SubmitPromptRequest_CarriesExplicitHeadlessContextAttachmentsAndApprovals()
    {
        var context = new WorkThreadCommandContext
        {
            ProjectId = "project-1",
            ProjectPath = @"C:\repo",
            ThreadDraftId = "draft-1",
            ThreadId = "thread-1",
            PromptSessionId = "prompt-1",
            ModelProviderId = "provider-1",
            ModelId = "model-1",
        };
        var attachment = new WorkThreadPromptAttachment
        {
            AttachmentId = "attachment-1",
            Path = @"C:\repo\image.png",
            DisplayName = "image.png",
            ContentType = "image/png",
            Content = new byte[] { 1, 2, 3 },
        };
        var approval = new WorkThreadApprovalContext
        {
            AutoApprove = true,
            ApprovalPolicyId = "policy-1",
            Metadata = new Dictionary<string, string> { ["scope"] = "test" },
        };
        var request = new SubmitWorkThreadPromptRequest
        {
            Context = context,
            Prompt = "Review the change.",
            Attachments = [attachment],
            Approval = approval,
            QueueIfBusy = true,
        };

        Assert.AreSame(context, request.Context);
        Assert.AreEqual("project-1", request.Context.ProjectId);
        Assert.AreEqual(@"C:\repo", request.Context.ProjectPath);
        Assert.AreEqual("draft-1", request.Context.ThreadDraftId);
        Assert.AreEqual("thread-1", request.Context.ThreadId);
        Assert.AreEqual("prompt-1", request.Context.PromptSessionId);
        Assert.AreEqual("provider-1", request.Context.ModelProviderId);
        Assert.AreEqual("model-1", request.Context.ModelId);
        Assert.AreEqual("Review the change.", request.Prompt);
        Assert.AreSame(attachment, request.Attachments[0]);
        Assert.AreSame(approval, request.Approval);
        Assert.IsTrue(request.QueueIfBusy);
    }

    [TestMethod]
    public void OrchestratorFacade_ExposesCommandsQueriesAndEventsWithoutActorReferences()
    {
        var methods = typeof(IWorkThreadOrchestrator)
            .GetMethods()
            .Select(static method => method.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "AbortAsync",
                "ActivateSkillAsync",
                "CompactAsync",
                "CreateDraftAsync",
                "GetThreadSnapshotAsync",
                "LaunchThreadAsync",
                "QueuePromptAsync",
                "SteerAsync",
                "StreamEventsAsync",
                "SubmitPromptAsync",
            },
            methods);
        Assert.IsFalse(typeof(IWorkThreadOrchestrator).FullName!.Contains("Actor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CommandResult_CanRecommendPromptRestoreOnFailure()
    {
        var thread = new WorkThreadDescriptorSnapshot
        {
            ThreadId = "thread-1",
            Title = "Thread",
            BackendId = "backend",
            BackendSessionId = "session",
            WorkingDirectory = "C:/project",
        };
        var result = new WorkThreadCommandResult
        {
            Outcome = WorkThreadCommandOutcomeKind.FailedWithRestoreRecommendation,
            Thread = thread,
            Message = "provider failed",
            ShouldRestorePrompt = true,
        };

        Assert.AreEqual(WorkThreadCommandOutcomeKind.FailedWithRestoreRecommendation, result.Outcome);
        Assert.AreSame(thread, result.Thread);
        Assert.AreEqual("provider failed", result.Message);
        Assert.IsTrue(result.ShouldRestorePrompt);
    }

    [TestMethod]
    [DataRow(WorkThreadCommandOutcomeKind.Submitted, false)]
    [DataRow(WorkThreadCommandOutcomeKind.Queued, false)]
    [DataRow(WorkThreadCommandOutcomeKind.Steered, false)]
    [DataRow(WorkThreadCommandOutcomeKind.Cancelled, true)]
    [DataRow(WorkThreadCommandOutcomeKind.Rejected, true)]
    [DataRow(WorkThreadCommandOutcomeKind.FailedWithRestoreRecommendation, true)]
    public void CommandResult_RepresentsPromptSubmissionOutcomes(
        WorkThreadCommandOutcomeKind outcome,
        bool shouldRestorePrompt)
    {
        var result = new WorkThreadCommandResult
        {
            Outcome = outcome,
            Message = outcome.ToString(),
            ShouldRestorePrompt = shouldRestorePrompt,
        };

        Assert.AreEqual(outcome, result.Outcome);
        Assert.AreEqual(outcome.ToString(), result.Message);
        Assert.AreEqual(shouldRestorePrompt, result.ShouldRestorePrompt);
    }

    [TestMethod]
    public void DescriptorSnapshot_CopiesDescriptorWithoutRetainingMutableInstance()
    {
        var descriptor = new WorkThreadDescriptor
        {
            ThreadId = "thread-1",
            Kind = WorkThreadKind.ProjectThread,
            BackendId = "backend",
            ProviderKey = "provider",
            BackendSessionId = "session",
            ProjectRef = "project-1",
            WorkingDirectory = "C:/project",
            Title = "Original",
            Status = WorkThreadStatus.Active,
            CreatedAt = DateTimeOffset.Parse("2026-05-05T10:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-05-05T11:00:00Z"),
            LastActiveAt = DateTimeOffset.Parse("2026-05-05T12:00:00Z"),
            StartedAt = DateTimeOffset.Parse("2026-05-05T10:30:00Z"),
            LatestSummary = "summary",
            MessageCount = 3,
            SourcePath = "thread.md",
            MarkdownBody = "body",
        };

        var snapshot = WorkThreadDescriptorSnapshot.FromDescriptor(descriptor);
        descriptor.Title = "Changed";
        descriptor.MessageCount = 4;

        Assert.AreEqual("thread-1", snapshot.ThreadId);
        Assert.AreEqual(WorkThreadKind.ProjectThread, snapshot.Kind);
        Assert.AreEqual("backend", snapshot.BackendId);
        Assert.AreEqual("provider", snapshot.ProviderKey);
        Assert.AreEqual("session", snapshot.BackendSessionId);
        Assert.AreEqual("project-1", snapshot.ProjectRef);
        Assert.AreEqual("C:/project", snapshot.WorkingDirectory);
        Assert.AreEqual("Original", snapshot.Title);
        Assert.AreEqual(3, snapshot.MessageCount);
        Assert.AreEqual("thread.md", snapshot.SourcePath);
        Assert.AreEqual("body", snapshot.MarkdownBody);
    }

    [TestMethod]
    public void LifecycleAndQueueEvents_CarryPluginProjectionMetadata()
    {
        var lifecycle = new WorkThreadLifecycleEvent
        {
            ThreadId = "thread-1",
            SequenceNumber = 7,
            Kind = WorkThreadLifecycleEventKind.SessionRekeyed,
            PromptSessionId = "prompt-session-1",
            RunId = "run-1",
            PreviousId = "old-session",
            Message = "rekeyed",
        };
        var queue = new WorkThreadQueueChangedEvent
        {
            ThreadId = "thread-1",
            SequenceNumber = 8,
            QueueItemId = "queue-1",
            QueuedPromptCount = 2,
            IsEnqueued = true,
        };

        Assert.AreEqual(WorkThreadLifecycleEventKind.SessionRekeyed, lifecycle.Kind);
        Assert.AreEqual(7, lifecycle.SequenceNumber);
        Assert.AreEqual("old-session", lifecycle.PreviousId);
        Assert.AreEqual("run-1", lifecycle.RunId);
        Assert.AreEqual("queue-1", queue.QueueItemId);
        Assert.AreEqual(2, queue.QueuedPromptCount);
        Assert.IsTrue(queue.IsEnqueued);
    }
}
