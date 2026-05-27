using CodeAlta.Orchestration.Runtime;
using CodeAlta.Orchestration.Runtime.Plugins;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class PluginSessionOrchestrationServiceTests
{
    [TestMethod]
    public async Task SubmitPromptAsync_ForwardsExplicitSessionRequest()
    {
        var orchestrator = new FakeSessionOrchestrator();
        var service = new PluginSessionOrchestrationService(orchestrator);
        var request = new SubmitSessionPromptRequest
        {
            Context = CreateContext() with { SessionId = "session-1" },
            Prompt = "prompt",
        };

        var result = await service.SubmitPromptAsync(request);

        Assert.AreEqual(SessionCommandOutcomeKind.Submitted, result.Outcome);
        Assert.AreSame(request, orchestrator.SubmitRequest);
    }

    [TestMethod]
    public async Task LaunchSessionAsync_AllowsDraftOnlyContext()
    {
        var orchestrator = new FakeSessionOrchestrator();
        var service = new PluginSessionOrchestrationService(orchestrator);
        var request = new LaunchSessionRequest
        {
            Context = CreateContext() with { SessionDraftId = "draft-1" },
            Title = "Title",
        };

        var result = await service.LaunchSessionAsync(request);

        Assert.AreEqual(SessionCommandOutcomeKind.Completed, result.Outcome);
        Assert.AreSame(request, orchestrator.LaunchRequest);
    }

    [TestMethod]
    public void SubmitPromptAsync_RejectsMissingSessionReference()
    {
        var service = new PluginSessionOrchestrationService(new FakeSessionOrchestrator());
        var request = new SubmitSessionPromptRequest
        {
            Context = CreateContext(),
            Prompt = "prompt",
        };

        Assert.ThrowsExactly<ArgumentException>(() => service.SubmitPromptAsync(request).AsTask().GetAwaiter().GetResult());
    }

    [TestMethod]
    [DataRow("ProjectId")]
    [DataRow("ProjectPath")]
    [DataRow("PromptSessionId")]
    [DataRow("ModelProviderId")]
    [DataRow("SessionReference")]
    public void SubmitPromptAsync_RejectsMissingRequiredContext(string missingField)
    {
        var service = new PluginSessionOrchestrationService(new FakeSessionOrchestrator());
        var context = CreateContext() with { SessionId = "session-1" };
        context = missingField switch
        {
            "ProjectId" => context with { ProjectId = " " },
            "ProjectPath" => context with { ProjectPath = " " },
            "PromptSessionId" => context with { PromptSessionId = " " },
            "ModelProviderId" => context with { ModelProviderId = " " },
            "SessionReference" => context with { SessionId = null, SessionDraftId = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(missingField), missingField, "Unknown required context field."),
        };
        var request = new SubmitSessionPromptRequest
        {
            Context = context,
            Prompt = "prompt",
        };

        Assert.ThrowsExactly<ArgumentException>(() => service.SubmitPromptAsync(request).AsTask().GetAwaiter().GetResult());
    }

    [TestMethod]
    public void GetSessionSnapshotAsync_RejectsMissingSessionId()
    {
        var service = new PluginSessionOrchestrationService(new FakeSessionOrchestrator());

        Assert.ThrowsExactly<ArgumentException>(() => service.GetSessionSnapshotAsync(" ").AsTask().GetAwaiter().GetResult());
    }

    private static SessionCommandContext CreateContext()
        => new()
        {
            ProjectId = "project-1",
            ProjectPath = "C:/project",
            PromptSessionId = "prompt-session-1",
            ModelProviderId = "provider-1",
        };

    private sealed class FakeSessionOrchestrator : ISessionOrchestrator
    {
        public LaunchSessionRequest? LaunchRequest { get; private set; }

        public SubmitSessionPromptRequest? SubmitRequest { get; private set; }

        public ValueTask<SessionCommandResult> CreateDraftAsync(CreateSessionDraftRequest request, CancellationToken cancellationToken = default)
            => new(new SessionCommandResult { Outcome = SessionCommandOutcomeKind.Completed });

        public ValueTask<SessionCommandResult> LaunchSessionAsync(LaunchSessionRequest request, CancellationToken cancellationToken = default)
        {
            LaunchRequest = request;
            return new(new SessionCommandResult { Outcome = SessionCommandOutcomeKind.Completed });
        }

        public ValueTask<SessionCommandResult> SubmitPromptAsync(SubmitSessionPromptRequest request, CancellationToken cancellationToken = default)
        {
            SubmitRequest = request;
            return new(new SessionCommandResult { Outcome = SessionCommandOutcomeKind.Submitted });
        }

        public ValueTask<SessionCommandResult> SteerAsync(SteerSessionRequest request, CancellationToken cancellationToken = default)
            => new(new SessionCommandResult { Outcome = SessionCommandOutcomeKind.Steered });

        public ValueTask<SessionCommandResult> AbortAsync(AbortSessionRequest request, CancellationToken cancellationToken = default)
            => new(new SessionCommandResult { Outcome = SessionCommandOutcomeKind.Completed });

        public ValueTask<SessionCommandResult> CompactAsync(CompactSessionRequest request, CancellationToken cancellationToken = default)
            => new(new SessionCommandResult { Outcome = SessionCommandOutcomeKind.Completed });

        public ValueTask<SessionCommandResult> ActivateSkillAsync(ActivateSkillRequest request, CancellationToken cancellationToken = default)
            => new(new SessionCommandResult { Outcome = SessionCommandOutcomeKind.Completed });

        public ValueTask<SessionCommandResult> QueuePromptAsync(QueueSessionPromptRequest request, CancellationToken cancellationToken = default)
            => new(new SessionCommandResult { Outcome = SessionCommandOutcomeKind.Queued });

        public ValueTask<SessionSnapshot?> GetSessionSnapshotAsync(string sessionId, CancellationToken cancellationToken = default)
            => new((SessionSnapshot?)null);

        public async IAsyncEnumerable<SessionOrchestratorEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
