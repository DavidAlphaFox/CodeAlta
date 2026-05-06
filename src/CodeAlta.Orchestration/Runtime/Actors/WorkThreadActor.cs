namespace CodeAlta.Orchestration.Runtime.Actors;

internal sealed record WorkThreadActorCommandResult
{
    public required bool Succeeded { get; init; }

    public string? Message { get; init; }

    public Exception? Exception { get; init; }

    public static WorkThreadActorCommandResult Success(string? message = null)
        => new() { Succeeded = true, Message = message };

    public static WorkThreadActorCommandResult Failure(Exception exception, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new WorkThreadActorCommandResult
        {
            Succeeded = false,
            Message = message ?? exception.Message,
            Exception = exception,
        };
    }
}

internal enum WorkThreadActorSupervisorDecision
{
    Complete,
    FailCommand,
    StopActor,
}

internal sealed record WorkThreadActorSupervisorEvent(
    string ThreadId,
    WorkThreadActorSupervisorDecision Decision,
    Exception? Exception = null,
    string? Message = null);

internal sealed class WorkThreadActor : IAsyncDisposable
{
    private readonly OrchestrationMailboxActor _mailbox;
    private readonly Func<Exception, WorkThreadActorSupervisorDecision> _superviseException;
    private bool _stopped;

    public WorkThreadActor(
        string threadId,
        int mailboxCapacity = 128,
        Func<Exception, WorkThreadActorSupervisorDecision>? superviseException = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ThreadId = threadId;
        _mailbox = new OrchestrationMailboxActor(mailboxCapacity);
        _superviseException = superviseException ?? (_ => WorkThreadActorSupervisorDecision.FailCommand);
    }

    public string ThreadId { get; }

    public event EventHandler<WorkThreadActorSupervisorEvent>? SupervisorEventEmitted;

    public ValueTask PostAsync(Func<CancellationToken, ValueTask> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfStopped();
        return _mailbox.PostAsync(command, cancellationToken);
    }

    public async ValueTask<WorkThreadActorCommandResult> ExecuteAsync(
        Func<CancellationToken, ValueTask> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfStopped();

        return await ExecuteCoreAsync(command, reserved: false, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<WorkThreadActorCommandResult> ExecuteReservedAsync(
        Func<CancellationToken, ValueTask> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfStopped();

        return await ExecuteCoreAsync(command, reserved: true, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<WorkThreadActorCommandResult> ExecuteCoreAsync(
        Func<CancellationToken, ValueTask> command,
        bool reserved,
        CancellationToken cancellationToken)
    {
        ValueTask<WorkThreadActorCommandResult> Execute(Func<CancellationToken, ValueTask<WorkThreadActorCommandResult>> query)
            => reserved
                ? _mailbox.AskReservedAsync(query, cancellationToken)
                : _mailbox.AskAsync(query, cancellationToken);

        return await Execute(
            async actorCancellationToken =>
            {
                try
                {
                    await command(actorCancellationToken).ConfigureAwait(false);
                    Emit(WorkThreadActorSupervisorDecision.Complete);
                    return WorkThreadActorCommandResult.Success();
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !actorCancellationToken.IsCancellationRequested)
                {
                    var decision = _superviseException(exception);
                    Emit(decision, exception, exception.Message);
                    if (decision == WorkThreadActorSupervisorDecision.StopActor)
                    {
                        _ = StopAsync(cancelPending: true).AsTask();
                    }

                    return WorkThreadActorCommandResult.Failure(exception);
                }
            }).ConfigureAwait(false);
    }

    public async ValueTask<TReply> QueryAsync<TReply>(
        Func<CancellationToken, ValueTask<TReply>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfStopped();
        return await _mailbox.AskAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(bool cancelPending = false)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        await _mailbox.StopAsync(cancelPending).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
        => await StopAsync(cancelPending: true).ConfigureAwait(false);

    private void Emit(
        WorkThreadActorSupervisorDecision decision,
        Exception? exception = null,
        string? message = null)
        => SupervisorEventEmitted?.Invoke(this, new WorkThreadActorSupervisorEvent(ThreadId, decision, exception, message));

    private void ThrowIfStopped()
    {
        if (_stopped)
        {
            throw new ObjectDisposedException(nameof(WorkThreadActor));
        }
    }
}
