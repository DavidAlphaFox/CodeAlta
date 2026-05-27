namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Exposes explicit session-view orchestration commands for plugins that must not rely on a selected frontend session.
/// </summary>
public interface IPluginSessionOrchestrationService
{
    /// <summary>Launches or materializes a session view from an explicit request.</summary>
    ValueTask<SessionCommandResult> LaunchSessionAsync(LaunchSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Submits a prompt to an explicit session-view context.</summary>
    ValueTask<SessionCommandResult> SubmitPromptAsync(SubmitSessionPromptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Steers an explicit session-view run.</summary>
    ValueTask<SessionCommandResult> SteerAsync(SteerSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Queues a prompt for an explicit session-view context.</summary>
    ValueTask<SessionCommandResult> QueuePromptAsync(QueueSessionPromptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets an immutable snapshot for an explicit durable session view.</summary>
    ValueTask<SessionSnapshot?> GetSessionSnapshotAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Plugin-facing adapter over the headless session-view orchestrator facade.
/// </summary>
public sealed class PluginSessionOrchestrationService : IPluginSessionOrchestrationService
{
    private readonly ISessionOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginSessionOrchestrationService"/> class.
    /// </summary>
    /// <param name="orchestrator">The headless session-view orchestrator.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="orchestrator"/> is <see langword="null"/>.</exception>
    public PluginSessionOrchestrationService(ISessionOrchestrator orchestrator)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        _orchestrator = orchestrator;
    }

    /// <inheritdoc />
    public ValueTask<SessionCommandResult> LaunchSessionAsync(LaunchSessionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateContext(request?.Context, requireSessionReference: false);
        return _orchestrator.LaunchSessionAsync(request!, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<SessionCommandResult> SubmitPromptAsync(SubmitSessionPromptRequest request, CancellationToken cancellationToken = default)
    {
        ValidateContext(request?.Context, requireSessionReference: true);
        return _orchestrator.SubmitPromptAsync(request!, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<SessionCommandResult> SteerAsync(SteerSessionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateContext(request?.Context, requireSessionReference: true);
        return _orchestrator.SteerAsync(request!, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<SessionCommandResult> QueuePromptAsync(QueueSessionPromptRequest request, CancellationToken cancellationToken = default)
    {
        ValidateContext(request?.Context, requireSessionReference: true);
        return _orchestrator.QueuePromptAsync(request!, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<SessionSnapshot?> GetSessionSnapshotAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _orchestrator.GetSessionSnapshotAsync(sessionId, cancellationToken);
    }

    private static void ValidateContext(SessionCommandContext? context, bool requireSessionReference)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.PromptSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ModelProviderId);

        if (!requireSessionReference)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(context.SessionId) && string.IsNullOrWhiteSpace(context.SessionDraftId))
        {
            throw new ArgumentException("Plugin session orchestration commands require an explicit session id or session draft id.", nameof(context));
        }
    }
}
