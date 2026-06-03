namespace CodeAlta.Agent;

// 模块功能：缓存并懒加载 Agent 会话列表快照，支持过滤、失效和删除通知
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
    public async Task<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var deleted = await _store.DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await NotifySessionDeletedAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    /// <inheritdoc />
    public Task NotifySessionUpdatedAsync(string sessionId, CancellationToken cancellationToken = default)
        => InvalidateAsync(sessionId, cancellationToken);

    // 函数功能：在锁保护下返回已有快照或已启动的加载任务，避免重复加载
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

    // 函数功能：从存储加载全量会话列表，按更新时间降序排序后写入快照缓存；失败时清除加载任务
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

    // 函数功能：检查会话是否满足过滤条件（Cwd、GitRoot、Repository、Branch 均支持）
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
