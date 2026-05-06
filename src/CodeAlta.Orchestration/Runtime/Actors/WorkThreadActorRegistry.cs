using System.Collections.Concurrent;

namespace CodeAlta.Orchestration.Runtime.Actors;

internal sealed class WorkThreadActorRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, WorkThreadActor> _actors = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _mailboxCapacity;
    private readonly Func<Exception, WorkThreadActorSupervisorDecision>? _superviseException;
    private bool _disposed;

    public WorkThreadActorRegistry(
        int mailboxCapacity = 128,
        Func<Exception, WorkThreadActorSupervisorDecision>? superviseException = null)
    {
        if (mailboxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mailboxCapacity), mailboxCapacity, "Mailbox capacity must be greater than zero.");
        }

        _mailboxCapacity = mailboxCapacity;
        _superviseException = superviseException;
    }

    public int Count => _actors.Count;

    public WorkThreadActor GetOrCreate(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ThrowIfDisposed();
        return _actors.GetOrAdd(threadId, static (id, state) =>
            new WorkThreadActor(id, state.MailboxCapacity, state.SuperviseException),
            (MailboxCapacity: _mailboxCapacity, SuperviseException: _superviseException));
    }

    public bool TryGet(string threadId, out WorkThreadActor actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ThrowIfDisposed();
        return _actors.TryGetValue(threadId, out actor!);
    }

    public async ValueTask<bool> RemoveAsync(string threadId, bool cancelPending = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ThrowIfDisposed();
        if (!_actors.TryRemove(threadId, out var actor))
        {
            return false;
        }

        await actor.StopAsync(cancelPending).ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var actors = _actors.Values.ToArray();
        _actors.Clear();
        foreach (var actor in actors)
        {
            await actor.StopAsync(cancelPending: true).ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorkThreadActorRegistry));
        }
    }
}
