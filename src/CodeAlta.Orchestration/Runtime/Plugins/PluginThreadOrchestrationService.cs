namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Exposes explicit work-thread orchestration commands for plugins that must not rely on a selected frontend thread.
/// </summary>
public interface IPluginThreadOrchestrationService
{
    /// <summary>Launches or materializes a work thread from an explicit request.</summary>
    ValueTask<WorkThreadCommandResult> LaunchThreadAsync(LaunchWorkThreadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Submits a prompt to an explicit work-thread context.</summary>
    ValueTask<WorkThreadCommandResult> SubmitPromptAsync(SubmitWorkThreadPromptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Steers an explicit work-thread run.</summary>
    ValueTask<WorkThreadCommandResult> SteerAsync(SteerWorkThreadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Queues a prompt for an explicit work-thread context.</summary>
    ValueTask<WorkThreadCommandResult> QueuePromptAsync(QueueWorkThreadPromptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets an immutable snapshot for an explicit durable work thread.</summary>
    ValueTask<WorkThreadSnapshot?> GetThreadSnapshotAsync(string threadId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Plugin-facing adapter over the headless work-thread orchestrator facade.
/// </summary>
public sealed class PluginThreadOrchestrationService : IPluginThreadOrchestrationService
{
    private readonly IWorkThreadOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginThreadOrchestrationService"/> class.
    /// </summary>
    /// <param name="orchestrator">The headless work-thread orchestrator.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="orchestrator"/> is <see langword="null"/>.</exception>
    public PluginThreadOrchestrationService(IWorkThreadOrchestrator orchestrator)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        _orchestrator = orchestrator;
    }

    /// <inheritdoc />
    public ValueTask<WorkThreadCommandResult> LaunchThreadAsync(LaunchWorkThreadRequest request, CancellationToken cancellationToken = default)
    {
        ValidateContext(request?.Context, requireThreadReference: false);
        return _orchestrator.LaunchThreadAsync(request!, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<WorkThreadCommandResult> SubmitPromptAsync(SubmitWorkThreadPromptRequest request, CancellationToken cancellationToken = default)
    {
        ValidateContext(request?.Context, requireThreadReference: true);
        return _orchestrator.SubmitPromptAsync(request!, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<WorkThreadCommandResult> SteerAsync(SteerWorkThreadRequest request, CancellationToken cancellationToken = default)
    {
        ValidateContext(request?.Context, requireThreadReference: true);
        return _orchestrator.SteerAsync(request!, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<WorkThreadCommandResult> QueuePromptAsync(QueueWorkThreadPromptRequest request, CancellationToken cancellationToken = default)
    {
        ValidateContext(request?.Context, requireThreadReference: true);
        return _orchestrator.QueuePromptAsync(request!, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<WorkThreadSnapshot?> GetThreadSnapshotAsync(string threadId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return _orchestrator.GetThreadSnapshotAsync(threadId, cancellationToken);
    }

    private static void ValidateContext(WorkThreadCommandContext? context, bool requireThreadReference)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.PromptSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ModelProviderId);

        if (!requireThreadReference)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(context.ThreadId) && string.IsNullOrWhiteSpace(context.ThreadDraftId))
        {
            throw new ArgumentException("Plugin thread orchestration commands require an explicit thread id or thread draft id.", nameof(context));
        }
    }
}
