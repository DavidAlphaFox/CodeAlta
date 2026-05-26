namespace CodeAlta.Agent;

/// <summary>
/// Caches a provider-independent snapshot of persisted CodeAlta agent sessions.
/// </summary>
public sealed class AgentSessionCatalog : IAgentSessionCatalog
{
    private readonly IAgentSessionStore _store;
    private readonly object _gate = new();
    private IReadOnlyList<AgentSessionMetadata>? _snapshot;
    private Task<IReadOnlyList<AgentSessionMetadata>>? _loadTask;
    private long _version;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentSessionCatalog"/> class.
    /// </summary>
    /// <param name="store">Provider-independent session store.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store" /> is <see langword="null" />.</exception>
    public AgentSessionCatalog(IAgentSessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentSessionMetadata> ListSessionsAsync(
        AgentSessionListFilter? filter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var loadTask = GetOrStartLoadTask();
        var sessions = await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MatchesFilter(session, filter))
            {
                yield return session;
            }
        }
    }

    /// <inheritdoc />
    public Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _version++;
            _snapshot = null;
            _loadTask = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvalidateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return InvalidateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task NotifySessionCreatedAsync(string sessionId, CancellationToken cancellationToken = default)
        => InvalidateAsync(sessionId, cancellationToken);

    /// <inheritdoc />
    public Task NotifySessionResumedAsync(string sessionId, CancellationToken cancellationToken = default)
        => InvalidateAsync(sessionId, cancellationToken);

    /// <inheritdoc />
    public Task NotifySessionDeletedAsync(string sessionId, CancellationToken cancellationToken = default)
        => InvalidateAsync(sessionId, cancellationToken);

    /// <inheritdoc />
    public Task NotifySessionUpdatedAsync(string sessionId, CancellationToken cancellationToken = default)
        => InvalidateAsync(sessionId, cancellationToken);

    private Task<IReadOnlyList<AgentSessionMetadata>> GetOrStartLoadTask()
    {
        lock (_gate)
        {
            if (_snapshot is not null)
            {
                return Task.FromResult(_snapshot);
            }

            if (_loadTask is not null)
            {
                return _loadTask;
            }

            var version = _version;
            _loadTask = LoadSnapshotAsync(version);
            return _loadTask;
        }
    }

    private async Task<IReadOnlyList<AgentSessionMetadata>> LoadSnapshotAsync(long version)
    {
        try
        {
            var sessions = new List<AgentSessionMetadata>();
            await foreach (var session in _store.ListSessionsAsync(filter: null, CancellationToken.None).ConfigureAwait(false))
            {
                sessions.Add(session);
            }

            var snapshot = sessions
                .OrderByDescending(static session => session.UpdatedAt)
                .ThenByDescending(static session => session.SessionId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            lock (_gate)
            {
                if (_version == version)
                {
                    _snapshot = snapshot;
                    _loadTask = null;
                }
            }

            return snapshot;
        }
        catch
        {
            lock (_gate)
            {
                if (_version == version)
                {
                    _loadTask = null;
                }
            }

            throw;
        }
    }

    private static bool MatchesFilter(AgentSessionMetadata session, AgentSessionListFilter? filter)
    {
        if (filter is null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filter.Cwd) &&
            !string.Equals(session.Context?.Cwd ?? session.WorkspacePath, filter.Cwd, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.GitRoot) &&
            !string.Equals(session.Context?.GitRoot, filter.GitRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Repository) &&
            !string.Equals(session.Context?.Repository, filter.Repository, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Branch) &&
            !string.Equals(session.Context?.Branch, filter.Branch, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
