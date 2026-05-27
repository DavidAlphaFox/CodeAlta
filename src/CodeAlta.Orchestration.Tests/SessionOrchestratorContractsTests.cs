using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class SessionOrchestratorContractsTests
{
    [TestMethod]
    public void SubmitPromptRequest_CarriesExplicitHeadlessContextAttachmentsAndApprovals()
    {
        var context = new SessionCommandContext
        {
            ProjectId = "project-1",
            ProjectPath = @"C:\repo",
            SessionDraftId = "draft-1",
            SessionId = "session-1",
            PromptSessionId = "prompt-1",
            ModelProviderId = "provider-1",
            ModelId = "model-1",
        };
        var attachment = new SessionPromptAttachment
        {
            AttachmentId = "attachment-1",
            Path = @"C:\repo\image.png",
            DisplayName = "image.png",
            ContentType = "image/png",
            Content = new byte[] { 1, 2, 3 },
        };
        var approval = new SessionApprovalContext
        {
            AutoApprove = true,
            ApprovalPolicyId = "policy-1",
            Metadata = new Dictionary<string, string> { ["scope"] = "test" },
        };
        var request = new SubmitSessionPromptRequest
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
        Assert.AreEqual("draft-1", request.Context.SessionDraftId);
        Assert.AreEqual("session-1", request.Context.SessionId);
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
        var methods = typeof(ISessionOrchestrator)
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
                "GetSessionSnapshotAsync",
                "LaunchSessionAsync",
                "QueuePromptAsync",
                "SteerAsync",
                "StreamEventsAsync",
                "SubmitPromptAsync",
            },
            methods);
        Assert.IsFalse(typeof(ISessionOrchestrator).FullName!.Contains("Actor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CommandResult_CanRecommendPromptRestoreOnFailure()
    {
        var session = new SessionViewDescriptorSnapshot
        {
            SessionId = "session-1",
            Title = "Session",
            ProviderId = "backend",
            WorkingDirectory = "C:/project",
        };
        var result = new SessionCommandResult
        {
            Outcome = SessionCommandOutcomeKind.FailedWithRestoreRecommendation,
            Session = session,
            Message = "provider failed",
            ShouldRestorePrompt = true,
        };

        Assert.AreEqual(SessionCommandOutcomeKind.FailedWithRestoreRecommendation, result.Outcome);
        Assert.AreSame(session, result.Session);
        Assert.AreEqual("provider failed", result.Message);
        Assert.IsTrue(result.ShouldRestorePrompt);
    }

    [TestMethod]
    [DataRow(SessionCommandOutcomeKind.Submitted, false)]
    [DataRow(SessionCommandOutcomeKind.Queued, false)]
    [DataRow(SessionCommandOutcomeKind.Steered, false)]
    [DataRow(SessionCommandOutcomeKind.Cancelled, true)]
    [DataRow(SessionCommandOutcomeKind.Rejected, true)]
    [DataRow(SessionCommandOutcomeKind.FailedWithRestoreRecommendation, true)]
    public void CommandResult_RepresentsPromptSubmissionOutcomes(
        SessionCommandOutcomeKind outcome,
        bool shouldRestorePrompt)
    {
        var result = new SessionCommandResult
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
        var descriptor = new SessionViewDescriptor
        {
            SessionId = "session-1",
            Kind = SessionViewKind.ProjectSession,
            ProviderId = "backend",
            ProviderKey = "provider",
            ProjectRef = "project-1",
            WorkingDirectory = "C:/project",
            Title = "Original",
            Status = SessionViewStatus.Active,
            CreatedAt = DateTimeOffset.Parse("2026-05-05T10:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-05-05T11:00:00Z"),
            LastActiveAt = DateTimeOffset.Parse("2026-05-05T12:00:00Z"),
            StartedAt = DateTimeOffset.Parse("2026-05-05T10:30:00Z"),
            LatestSummary = "summary",
            MessageCount = 3,
            SourcePath = "session.md",
            MarkdownBody = "body",
        };

        var snapshot = SessionViewDescriptorSnapshot.FromDescriptor(descriptor);
        descriptor.Title = "Changed";
        descriptor.MessageCount = 4;

        Assert.AreEqual("session-1", snapshot.SessionId);
        Assert.AreEqual(SessionViewKind.ProjectSession, snapshot.Kind);
        Assert.AreEqual("backend", snapshot.ProviderId);
        Assert.AreEqual("provider", snapshot.ProviderKey);
        Assert.AreEqual("project-1", snapshot.ProjectRef);
        Assert.AreEqual("C:/project", snapshot.WorkingDirectory);
        Assert.AreEqual("Original", snapshot.Title);
        Assert.AreEqual(3, snapshot.MessageCount);
        Assert.AreEqual("session.md", snapshot.SourcePath);
        Assert.AreEqual("body", snapshot.MarkdownBody);
    }

    [TestMethod]
    public void LifecycleAndQueueEvents_CarryPluginProjectionMetadata()
    {
        var lifecycle = new SessionLifecycleEvent
        {
            SessionId = "session-1",
            SequenceNumber = 7,
            Kind = SessionLifecycleEventKind.SessionRekeyed,
            PromptSessionId = "prompt-session-1",
            RunId = "run-1",
            PreviousId = "old-session",
            Message = "rekeyed",
        };
        var queue = new SessionQueueChangedEvent
        {
            SessionId = "session-1",
            SequenceNumber = 8,
            QueueItemId = "queue-1",
            QueuedPromptCount = 2,
            IsEnqueued = true,
        };

        Assert.AreEqual(SessionLifecycleEventKind.SessionRekeyed, lifecycle.Kind);
        Assert.AreEqual(7, lifecycle.SequenceNumber);
        Assert.AreEqual("old-session", lifecycle.PreviousId);
        Assert.AreEqual("run-1", lifecycle.RunId);
        Assert.AreEqual("queue-1", queue.QueueItemId);
        Assert.AreEqual(2, queue.QueuedPromptCount);
        Assert.IsTrue(queue.IsEnqueued);
    }
}
