namespace CodeAlta.Catalog;

/// <summary>
/// Implements stale-while-revalidate project file and directory search.
/// </summary>
public sealed class ProjectFileSearchService : IProjectFileSearchService
{
    private readonly IProjectFileSnapshotCache _snapshotCache;
    private readonly IProjectFileUsageStore _usageStore;
    private readonly IProjectFileSearchTraversal _traversal;
    private readonly ProjectFileSearchScorer _scorer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectFileSearchService"/> class.
    /// </summary>
    /// <param name="snapshotCache">Shared snapshot cache.</param>
    /// <param name="usageStore">Recent-usage store.</param>
    /// <exception cref="ArgumentNullException">Thrown when required arguments are <see langword="null"/>.</exception>
    public ProjectFileSearchService(
        IProjectFileSnapshotCache snapshotCache,
        IProjectFileUsageStore usageStore)
        : this(snapshotCache, usageStore, new XenoAtomGlobProjectFileSearchTraversal(), new ProjectFileSearchScorer())
    {
    }

    public ProjectFileSearchService(
        IProjectFileSnapshotCache snapshotCache,
        IProjectFileUsageStore usageStore,
        IProjectFileSearchTraversal traversal,
        ProjectFileSearchScorer scorer)
    {
        ArgumentNullException.ThrowIfNull(snapshotCache);
        ArgumentNullException.ThrowIfNull(usageStore);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(scorer);

        _snapshotCache = snapshotCache;
        _usageStore = usageStore;
        _traversal = traversal;
        _scorer = scorer;
    }

    /// <inheritdoc />
    public async ValueTask<IProjectFileSearchSession> CreateSessionAsync(
        ProjectFileSearchSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalizedRoot = ProjectFilePathUtilities.NormalizeProjectRoot(options.ProjectRoot);
        var recentTask = _usageStore.GetRecentAsync(normalizedRoot, Math.Max(1, options.RecentItemLimit), cancellationToken).AsTask();
        var cacheTask = _snapshotCache.GetAsync(normalizedRoot, cancellationToken).AsTask();
        await Task.WhenAll(recentTask, cacheTask).ConfigureAwait(false);

        var session = new ProjectFileSearchSession(
            normalizedRoot,
            options with { ProjectRoot = normalizedRoot },
            _snapshotCache,
            _usageStore,
            _traversal,
            _scorer,
            recentTask.Result,
            cacheTask.Result);
        await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <inheritdoc />
    public async ValueTask<ProjectFileResolution> ResolveAsync(
        ProjectFileResolveQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var projectRoot = ProjectFilePathUtilities.NormalizeProjectRoot(query.ProjectRoot);
        var normalizedPath = ProjectFilePathUtilities.NormalizeStoredRelativePath(query.ReferenceText);
        var cacheEntry = await _snapshotCache.GetAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var cachedItem = cacheEntry?.Snapshot?.Items.FirstOrDefault(
            item => string.Equals(item.RelativePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (cachedItem is not null)
        {
            return new ProjectFileResolution(true, normalizedPath, cachedItem, query.LineRange);
        }

        var usageByPath = await _usageStore.GetUsageByRelativePathAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var fullPath = ProjectFilePathUtilities.BuildFullPath(projectRoot, normalizedPath);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            usageByPath.TryGetValue(normalizedPath, out var usage);
            var kind = Directory.Exists(fullPath) ? ProjectFileSearchItemKind.Directory : ProjectFileSearchItemKind.File;
            var basename = Path.GetFileName(normalizedPath);
            var extension = kind == ProjectFileSearchItemKind.Directory ? string.Empty : Path.GetExtension(basename);
            var parentPath = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/') ?? string.Empty;
            var item = new ProjectFileSearchItem
            {
                Kind = kind,
                ProjectRoot = projectRoot,
                RelativePath = normalizedPath,
                FullPath = fullPath,
                Basename = basename,
                ParentPath = parentPath,
                Extension = extension,
                LastWriteTimeUtc = ProjectFilePathUtilities.TryGetLastWriteTimeUtc(fullPath, kind),
                SearchFields = ProjectFilePathUtilities.CreateSearchFields(normalizedPath, basename, extension),
                Usage = usage,
            };
            return new ProjectFileResolution(true, normalizedPath, item, query.LineRange);
        }

        return new ProjectFileResolution(false, normalizedPath, Item: null, query.LineRange);
    }

    /// <inheritdoc />
    public ValueTask RecordUsageAsync(
        ProjectFileUsageEvent usageEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usageEvent);
        return _usageStore.RecordAsync(usageEvent, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask InvalidateAsync(
        string projectRoot,
        ProjectFileInvalidationReason reason,
        CancellationToken cancellationToken = default)
    {
        return _snapshotCache.MarkDirtyAsync(
            ProjectFilePathUtilities.NormalizeProjectRoot(projectRoot),
            reason,
            cancellationToken);
    }
}
