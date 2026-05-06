namespace CodeAlta.Catalog;

internal sealed class ProjectFileSearchSession : IProjectFileSearchSession
{
    private readonly object _gate = new();
    private readonly string _projectRoot;
    private readonly ProjectFileSearchSessionOptions _options;
    private readonly IProjectFileSnapshotCache _snapshotCache;
    private readonly IProjectFileUsageStore _usageStore;
    private readonly IProjectFileSearchTraversal _traversal;
    private readonly ProjectFileSearchScorer _scorer;
    private ProjectFileSearchState _current;
    private IReadOnlyList<ProjectFileSearchItem> _latestCandidates;
    private string _query;
    private CancellationTokenSource? _refreshCancellation;
    private long _refreshGeneration;
    private long _rankingRequestVersion;
    private RankingRequest? _pendingRankingRequest;
    private bool _rankingWorkerScheduled;
    private bool _disposed;

    public ProjectFileSearchSession(
        string projectRoot,
        ProjectFileSearchSessionOptions options,
        IProjectFileSnapshotCache snapshotCache,
        IProjectFileUsageStore usageStore,
        IProjectFileSearchTraversal traversal,
        ProjectFileSearchScorer scorer,
        IReadOnlyList<ProjectFileUsageEntry> recentUsage,
        ProjectFileSnapshotCacheEntry? cacheEntry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshotCache);
        ArgumentNullException.ThrowIfNull(usageStore);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(scorer);
        ArgumentNullException.ThrowIfNull(recentUsage);

        _projectRoot = projectRoot;
        _options = options;
        _snapshotCache = snapshotCache;
        _usageStore = usageStore;
        _traversal = traversal;
        _scorer = scorer;
        _query = options.Query ?? string.Empty;

        var recentSeedItems = recentUsage
            .Select(entry => ProjectFilePathUtilities.CreateSeedItem(projectRoot, entry))
            .ToArray();
        var cachedSnapshot = cacheEntry?.Snapshot;
        _latestCandidates = cachedSnapshot?.Items ?? recentSeedItems;
        _current = new ProjectFileSearchState
        {
            Query = _query,
            Results = _scorer.Rank(_query, _latestCandidates, Math.Max(1, options.MaximumResults)),
            IsRefreshing = false,
            HasSnapshot = cachedSnapshot is not null,
            RefreshGeneration = 0,
            SnapshotGeneration = cachedSnapshot?.SnapshotGeneration ?? 0,
            CandidateCount = _latestCandidates.Count,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public ProjectFileSearchState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<ProjectFileSearchStateChangedEventArgs>? Updated;

    public ValueTask SetQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ProjectFileSearchItem> candidates;
        long refreshGeneration;
        long snapshotGeneration;
        bool hasSnapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            _query = query ?? string.Empty;
            candidates = _latestCandidates;
            refreshGeneration = _refreshGeneration;
            snapshotGeneration = _current.SnapshotGeneration;
            hasSnapshot = _current.HasSnapshot;
        }

        ScheduleRanking(candidates, refreshGeneration, snapshotGeneration, hasSnapshot, Current.IsRefreshing);
        return ValueTask.CompletedTask;
    }

    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource linkedCancellation;
        long generation;
        long snapshotGeneration;
        bool hasSnapshot;
        IReadOnlyList<ProjectFileSearchItem> existingCandidates;
        lock (_gate)
        {
            ThrowIfDisposed();
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _refreshCancellation = linkedCancellation;
            generation = ++_refreshGeneration;
            snapshotGeneration = _current.SnapshotGeneration;
            hasSnapshot = _current.HasSnapshot;
            existingCandidates = _latestCandidates;
        }

        PublishState(
            new ProjectFileSearchState
            {
                Query = _query,
                Results = _scorer.Rank(_query, existingCandidates, Math.Max(1, _options.MaximumResults)),
                IsRefreshing = true,
                HasSnapshot = hasSnapshot,
                RefreshGeneration = generation,
                SnapshotGeneration = snapshotGeneration,
                CandidateCount = existingCandidates.Count,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        _ = RunRefreshAsync(generation, existingCandidates, linkedCancellation.Token);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _pendingRankingRequest = null;
        }

        return ValueTask.CompletedTask;
    }

    private async Task RunRefreshAsync(
        long refreshGeneration,
        IReadOnlyList<ProjectFileSearchItem> seedCandidates,
        CancellationToken cancellationToken)
    {
        try
        {
            await _snapshotCache.MarkDirtyAsync(_projectRoot, ProjectFileInvalidationReason.SessionRefresh, cancellationToken).ConfigureAwait(false);
            var usageByPath = await _usageStore.GetUsageByRelativePathAsync(_projectRoot, cancellationToken).ConfigureAwait(false);
            var working = new Dictionary<string, ProjectFileSearchItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in seedCandidates)
            {
                working[candidate.RelativePath] = candidate;
            }

            var traversalSnapshot = await Task.Run(
                () => _traversal.Traverse(
                    _projectRoot,
                    usageByPath,
                    Math.Max(1, _options.RefreshBatchSize),
                    batch =>
                    {
                        foreach (var item in batch)
                        {
                            working[item.RelativePath] = item;
                        }

                        ScheduleRanking(
                            working.Values.ToArray(),
                            refreshGeneration,
                            snapshotGeneration: 0,
                            hasSnapshot: false,
                            isRefreshing: true);
                    },
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var snapshot = new ProjectFileSnapshot
            {
                ProjectRoot = _projectRoot,
                IsGitAware = traversalSnapshot.IsGitAware,
                SnapshotGeneration = refreshGeneration,
                BuiltAt = DateTimeOffset.UtcNow,
                Items = traversalSnapshot.Items,
            };
            await _snapshotCache.SetAsync(snapshot, cancellationToken).ConfigureAwait(false);
            ScheduleRanking(
                traversalSnapshot.Items,
                refreshGeneration,
                snapshot.SnapshotGeneration,
                hasSnapshot: true,
                isRefreshing: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ScheduleRanking(
        IReadOnlyList<ProjectFileSearchItem> candidates,
        long refreshGeneration,
        long snapshotGeneration,
        bool hasSnapshot,
        bool isRefreshing)
    {
        bool shouldStartWorker;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingRankingRequest = new RankingRequest(
                Candidates: candidates,
                Query: _query,
                RefreshGeneration: refreshGeneration,
                SnapshotGeneration: snapshotGeneration,
                HasSnapshot: hasSnapshot,
                IsRefreshing: isRefreshing,
                RequestVersion: ++_rankingRequestVersion);
            shouldStartWorker = !_rankingWorkerScheduled;
            _rankingWorkerScheduled = true;
        }

        if (!shouldStartWorker)
        {
            return;
        }

        _ = Task.Run(RunRankingLoopAsync);
    }

    private void RunRankingLoopAsync()
    {
        while (true)
        {
            RankingRequest request;
            lock (_gate)
            {
                if (_disposed)
                {
                    _rankingWorkerScheduled = false;
                    return;
                }

                if (_pendingRankingRequest is not RankingRequest pendingRequest)
                {
                    _rankingWorkerScheduled = false;
                    return;
                }

                request = pendingRequest;
                _pendingRankingRequest = null;
            }

            var ranked = _scorer.Rank(request.Query, request.Candidates, Math.Max(1, _options.MaximumResults));

            lock (_gate)
            {
                if (_disposed)
                {
                    continue;
                }

                if (request.RefreshGeneration != _refreshGeneration ||
                    request.RequestVersion != _rankingRequestVersion)
                {
                    continue;
                }

                _latestCandidates = request.Candidates;
            }

            PublishState(
                new ProjectFileSearchState
                {
                    Query = request.Query,
                    Results = ranked,
                    IsRefreshing = request.IsRefreshing,
                    HasSnapshot = request.HasSnapshot,
                    RefreshGeneration = request.RefreshGeneration,
                    SnapshotGeneration = request.SnapshotGeneration,
                    CandidateCount = request.Candidates.Count,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
        }
    }

    private void PublishState(ProjectFileSearchState state)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _current = state;
        }

        Updated?.Invoke(this, new ProjectFileSearchStateChangedEventArgs(state));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProjectFileSearchSession));
        }
    }

    private readonly record struct RankingRequest(
        IReadOnlyList<ProjectFileSearchItem> Candidates,
        string Query,
        long RefreshGeneration,
        long SnapshotGeneration,
        bool HasSnapshot,
        bool IsRefreshing,
        long RequestVersion);
}
