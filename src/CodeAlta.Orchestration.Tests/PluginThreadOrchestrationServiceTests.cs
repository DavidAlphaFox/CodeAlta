using CodeAlta.Orchestration.Runtime;
using CodeAlta.Orchestration.Runtime.Plugins;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class PluginThreadOrchestrationServiceTests
{
    [TestMethod]
    public async Task SubmitPromptAsync_ForwardsExplicitThreadRequest()
    {
        var orchestrator = new FakeWorkThreadOrchestrator();
        var service = new PluginThreadOrchestrationService(orchestrator);
        var request = new SubmitWorkThreadPromptRequest
        {
            Context = CreateContext() with { ThreadId = "thread-1" },
            Prompt = "prompt",
        };

        var result = await service.SubmitPromptAsync(request);

        Assert.AreEqual(WorkThreadCommandOutcomeKind.Submitted, result.Outcome);
        Assert.AreSame(request, orchestrator.SubmitRequest);
    }

    [TestMethod]
    public async Task LaunchThreadAsync_AllowsDraftOnlyContext()
    {
        var orchestrator = new FakeWorkThreadOrchestrator();
        var service = new PluginThreadOrchestrationService(orchestrator);
        var request = new LaunchWorkThreadRequest
        {
            Context = CreateContext() with { ThreadDraftId = "draft-1" },
            Title = "Title",
        };

        var result = await service.LaunchThreadAsync(request);

        Assert.AreEqual(WorkThreadCommandOutcomeKind.Completed, result.Outcome);
        Assert.AreSame(request, orchestrator.LaunchRequest);
    }

    [TestMethod]
    public void SubmitPromptAsync_RejectsMissingThreadReference()
    {
        var service = new PluginThreadOrchestrationService(new FakeWorkThreadOrchestrator());
        var request = new SubmitWorkThreadPromptRequest
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
    [DataRow("ThreadReference")]
    public void SubmitPromptAsync_RejectsMissingRequiredContext(string missingField)
    {
        var service = new PluginThreadOrchestrationService(new FakeWorkThreadOrchestrator());
        var context = CreateContext() with { ThreadId = "thread-1" };
        context = missingField switch
        {
            "ProjectId" => context with { ProjectId = " " },
            "ProjectPath" => context with { ProjectPath = " " },
            "PromptSessionId" => context with { PromptSessionId = " " },
            "ModelProviderId" => context with { ModelProviderId = " " },
            "ThreadReference" => context with { ThreadId = null, ThreadDraftId = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(missingField), missingField, "Unknown required context field."),
        };
        var request = new SubmitWorkThreadPromptRequest
        {
            Context = context,
            Prompt = "prompt",
        };

        Assert.ThrowsExactly<ArgumentException>(() => service.SubmitPromptAsync(request).AsTask().GetAwaiter().GetResult());
    }

    [TestMethod]
    public void GetThreadSnapshotAsync_RejectsMissingThreadId()
    {
        var service = new PluginThreadOrchestrationService(new FakeWorkThreadOrchestrator());

        Assert.ThrowsExactly<ArgumentException>(() => service.GetThreadSnapshotAsync(" ").AsTask().GetAwaiter().GetResult());
    }

    private static WorkThreadCommandContext CreateContext()
        => new()
        {
            ProjectId = "project-1",
            ProjectPath = "C:/project",
            PromptSessionId = "prompt-session-1",
            ModelProviderId = "provider-1",
        };

    private sealed class FakeWorkThreadOrchestrator : IWorkThreadOrchestrator
    {
        public LaunchWorkThreadRequest? LaunchRequest { get; private set; }

        public SubmitWorkThreadPromptRequest? SubmitRequest { get; private set; }

        public ValueTask<WorkThreadCommandResult> CreateDraftAsync(CreateWorkThreadDraftRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

        public ValueTask<WorkThreadCommandResult> LaunchThreadAsync(LaunchWorkThreadRequest request, CancellationToken cancellationToken = default)
        {
            LaunchRequest = request;
            return new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });
        }

        public ValueTask<WorkThreadCommandResult> SubmitPromptAsync(SubmitWorkThreadPromptRequest request, CancellationToken cancellationToken = default)
        {
            SubmitRequest = request;
            return new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Submitted });
        }

        public ValueTask<WorkThreadCommandResult> SteerAsync(SteerWorkThreadRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Steered });

        public ValueTask<WorkThreadCommandResult> AbortAsync(AbortWorkThreadRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

        public ValueTask<WorkThreadCommandResult> CompactAsync(CompactWorkThreadRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

        public ValueTask<WorkThreadCommandResult> ActivateSkillAsync(ActivateSkillRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

        public ValueTask<WorkThreadCommandResult> QueuePromptAsync(QueueWorkThreadPromptRequest request, CancellationToken cancellationToken = default)
            => new(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Queued });

        public ValueTask<WorkThreadSnapshot?> GetThreadSnapshotAsync(string threadId, CancellationToken cancellationToken = default)
            => new((WorkThreadSnapshot?)null);

        public async IAsyncEnumerable<WorkThreadOrchestratorEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
