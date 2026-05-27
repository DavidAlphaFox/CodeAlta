using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.App;

internal sealed class RuntimeWorkThreadOrchestratorAdapter : IWorkThreadOrchestrator
{
    private readonly SessionRuntimeService _runtimeService;
    private readonly Func<string, SessionViewDescriptor?> _findThread;

    public RuntimeWorkThreadOrchestratorAdapter(
        SessionRuntimeService runtimeService,
        Func<string, SessionViewDescriptor?> findThread)
    {
        ArgumentNullException.ThrowIfNull(runtimeService);
        ArgumentNullException.ThrowIfNull(findThread);
        _runtimeService = runtimeService;
        _findThread = findThread;
    }

    public ValueTask<WorkThreadCommandResult> CreateDraftAsync(CreateWorkThreadDraftRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

    public ValueTask<WorkThreadCommandResult> LaunchThreadAsync(LaunchWorkThreadRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

    public async ValueTask<WorkThreadCommandResult> SubmitPromptAsync(SubmitWorkThreadPromptRequest request, CancellationToken cancellationToken = default)
    {
        var thread = ResolveThread(request.Context);
        var runId = await _runtimeService.SendAsync(
            thread,
            request.Context.ExecutionOptions ?? throw new ArgumentException("Execution options are required.", nameof(request)),
            new AgentSendOptions { Input = request.PreparedInput ?? AgentInput.Text(request.Prompt) },
            cancellationToken);
        return new WorkThreadCommandResult
        {
            Outcome = WorkThreadCommandOutcomeKind.Submitted,
            Thread = SessionViewDescriptorSnapshot.FromDescriptor(thread),
            RunId = runId.Value,
        };
    }

    public async ValueTask<WorkThreadCommandResult> SteerAsync(SteerWorkThreadRequest request, CancellationToken cancellationToken = default)
    {
        var thread = ResolveThread(request.Context);
        var runId = await _runtimeService.SteerAsync(
            thread,
            request.Context.ExecutionOptions ?? throw new ArgumentException("Execution options are required.", nameof(request)),
            new AgentSteerOptions { Input = request.PreparedInput ?? AgentInput.Text(request.Prompt) },
            cancellationToken);
        return new WorkThreadCommandResult
        {
            Outcome = WorkThreadCommandOutcomeKind.Steered,
            Thread = SessionViewDescriptorSnapshot.FromDescriptor(thread),
            RunId = runId.Value,
        };
    }

    public async ValueTask<WorkThreadCommandResult> AbortAsync(AbortWorkThreadRequest request, CancellationToken cancellationToken = default)
    {
        await _runtimeService.AbortAsync(request.ThreadId, cancellationToken);
        return new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed };
    }

    public async ValueTask<WorkThreadCommandResult> CompactAsync(CompactWorkThreadRequest request, CancellationToken cancellationToken = default)
    {
        var thread = ResolveThread(request.Context);
        await _runtimeService.CompactAsync(
            thread,
            request.Context.ExecutionOptions ?? throw new ArgumentException("Execution options are required.", nameof(request)),
            cancellationToken);
        return new WorkThreadCommandResult
        {
            Outcome = WorkThreadCommandOutcomeKind.Completed,
            Thread = SessionViewDescriptorSnapshot.FromDescriptor(thread),
        };
    }

    public ValueTask<WorkThreadCommandResult> ActivateSkillAsync(ActivateSkillRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new WorkThreadCommandResult { Outcome = WorkThreadCommandOutcomeKind.Completed });

    public async ValueTask<WorkThreadCommandResult> QueuePromptAsync(QueueWorkThreadPromptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var thread = ResolveThread(request.Context);
        var item = await _runtimeService.QueuePromptAsync(thread, request.Prompt, "send", submittedBy: null, cancellationToken);
        return new WorkThreadCommandResult
        {
            Outcome = WorkThreadCommandOutcomeKind.Queued,
            Thread = SessionViewDescriptorSnapshot.FromDescriptor(thread),
            Message = item.QueueItemId,
        };
    }

    public ValueTask<WorkThreadSnapshot?> GetThreadSnapshotAsync(string threadId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_findThread(threadId) is { } thread
            ? new WorkThreadSnapshot { Thread = SessionViewDescriptorSnapshot.FromDescriptor(thread), IsRunning = false, QueuedPromptCount = 0 }
            : null);

    public async IAsyncEnumerable<WorkThreadOrchestratorEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private SessionViewDescriptor ResolveThread(WorkThreadCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.ThreadId))
        {
            throw new ArgumentException("A materialized session id is required.", nameof(context));
        }

        return _findThread(context.ThreadId)
            ?? throw new InvalidOperationException($"Session '{context.ThreadId}' was not found.");
    }
}
