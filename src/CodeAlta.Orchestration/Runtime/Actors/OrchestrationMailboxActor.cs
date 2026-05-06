using System.Threading.Channels;

namespace CodeAlta.Orchestration.Runtime.Actors;

internal enum OrchestrationActorDiagnosticKind
{
    Started,
    CommandCompleted,
    CommandFailed,
    Stopped,
}

internal sealed record OrchestrationActorDiagnostic(
    OrchestrationActorDiagnosticKind Kind,
    Exception? Exception = null);

internal sealed class OrchestrationMailboxActor : IAsyncDisposable
{
    private readonly Channel<Func<CancellationToken, ValueTask>> _mailbox;
    private readonly SemaphoreSlim _normalSlots;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _runner;
    private bool _disposed;

    public OrchestrationMailboxActor(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Mailbox capacity must be greater than zero.");
        }

        _normalSlots = new SemaphoreSlim(capacity, capacity);
        _mailbox = Channel.CreateBounded<Func<CancellationToken, ValueTask>>(new BoundedChannelOptions(capacity + 1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _runner = Task.Run(RunAsync);
    }

    public event EventHandler<OrchestrationActorDiagnostic>? DiagnosticEmitted;

    public ValueTask PostAsync(
        Func<CancellationToken, ValueTask> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposed();
        return EnqueueAsync(command, reserveNormalSlot: true, cancellationToken);
    }

    public ValueTask PostReservedAsync(
        Func<CancellationToken, ValueTask> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposed();
        return EnqueueAsync(command, reserveNormalSlot: false, cancellationToken);
    }

    public async ValueTask<TReply> AskAsync<TReply>(
        Func<CancellationToken, ValueTask<TReply>> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposed();

        return await AskCoreAsync(command, reserveNormalSlot: true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TReply> AskReservedAsync<TReply>(
        Func<CancellationToken, ValueTask<TReply>> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposed();

        return await AskCoreAsync(command, reserveNormalSlot: false, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<TReply> AskCoreAsync<TReply>(
        Func<CancellationToken, ValueTask<TReply>> command,
        bool reserveNormalSlot,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<TReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueAsync(
                async actorCancellationToken =>
                {
                    try
                    {
                        completion.SetResult(await command(actorCancellationToken).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException) when (actorCancellationToken.IsCancellationRequested)
                    {
                        completion.SetCanceled(actorCancellationToken);
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                        throw;
                    }
                },
                reserveNormalSlot,
                cancellationToken)
            .ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnqueueAsync(
        Func<CancellationToken, ValueTask> command,
        bool reserveNormalSlot,
        CancellationToken cancellationToken)
    {
        var reserved = false;
        try
        {
            if (reserveNormalSlot)
            {
                await _normalSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                reserved = true;
            }

            await _mailbox.Writer.WriteAsync(Wrap(command, reserved), cancellationToken).ConfigureAwait(false);
            reserved = false;
        }
        finally
        {
            if (reserved)
            {
                _normalSlots.Release();
            }
        }
    }

    private Func<CancellationToken, ValueTask> Wrap(Func<CancellationToken, ValueTask> command, bool releaseNormalSlotOnStart)
        => async cancellationToken =>
        {
            if (releaseNormalSlotOnStart)
            {
                _normalSlots.Release();
            }

            await command(cancellationToken).ConfigureAwait(false);
        };

    public async ValueTask StopAsync(bool cancelPending = false)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (cancelPending)
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }

        _mailbox.Writer.TryComplete();
        await _runner.ConfigureAwait(false);
        _lifetime.Dispose();
        _normalSlots.Dispose();
    }

    public async ValueTask DisposeAsync()
        => await StopAsync(cancelPending: true).ConfigureAwait(false);

    private async Task RunAsync()
    {
        Emit(new OrchestrationActorDiagnostic(OrchestrationActorDiagnosticKind.Started));
        try
        {
            await foreach (var command in _mailbox.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                try
                {
                    await command(_lifetime.Token).ConfigureAwait(false);
                    Emit(new OrchestrationActorDiagnostic(OrchestrationActorDiagnosticKind.CommandCompleted));
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Emit(new OrchestrationActorDiagnostic(OrchestrationActorDiagnosticKind.CommandFailed, exception));
                }
            }
        }
        finally
        {
            Emit(new OrchestrationActorDiagnostic(OrchestrationActorDiagnosticKind.Stopped));
        }
    }

    private void Emit(OrchestrationActorDiagnostic diagnostic)
        => DiagnosticEmitted?.Invoke(this, diagnostic);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OrchestrationMailboxActor));
        }
    }
}
