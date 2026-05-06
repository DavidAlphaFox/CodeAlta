namespace CodeAlta.Catalog;

/// <summary>
/// Stores project-file usage in memory for ranking during the current process.
/// </summary>
public sealed class InMemoryProjectFileUsageStore : IProjectFileUsageStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string ProjectRoot, string RelativePath), ProjectFileUsageEntry> _entries = new(StringTupleComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask RecordAsync(ProjectFileUsageEvent usageEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usageEvent);
        cancellationToken.ThrowIfCancellationRequested();

        var projectRoot = ProjectFilePathUtilities.NormalizeProjectRoot(usageEvent.ProjectRoot);
        var relativePath = ProjectFilePathUtilities.NormalizeStoredRelativePath(usageEvent.RelativePath);
        var key = (projectRoot, relativePath);

        lock (_gate)
        {
            _entries.TryGetValue(key, out var existing);
            _entries[key] = new ProjectFileUsageEntry(
                projectRoot,
                relativePath,
                usageEvent.Kind,
                usageEvent.AccessedAt,
                (existing?.AccessCount ?? 0) + 1,
                usageEvent.AccessKind);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ProjectFileUsageEntry>> GetRecentAsync(string projectRoot, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit <= 0)
        {
            return ValueTask.FromResult<IReadOnlyList<ProjectFileUsageEntry>>([]);
        }

        projectRoot = ProjectFilePathUtilities.NormalizeProjectRoot(projectRoot);
        lock (_gate)
        {
            var entries = _entries.Values
                .Where(entry => string.Equals(entry.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.LastAccessedAt)
                .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToArray();
            return ValueTask.FromResult<IReadOnlyList<ProjectFileUsageEntry>>(entries);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, ProjectFileUsageEntry>> GetUsageByRelativePathAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        projectRoot = ProjectFilePathUtilities.NormalizeProjectRoot(projectRoot);
        lock (_gate)
        {
            var entries = _entries.Values
                .Where(entry => string.Equals(entry.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.RelativePath, entry => entry, StringComparer.OrdinalIgnoreCase);
            return ValueTask.FromResult<IReadOnlyDictionary<string, ProjectFileUsageEntry>>(entries);
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string ProjectRoot, string RelativePath)>
    {
        public static StringTupleComparer OrdinalIgnoreCase { get; } = new();

        public bool Equals((string ProjectRoot, string RelativePath) x, (string ProjectRoot, string RelativePath) y)
            => string.Equals(x.ProjectRoot, y.ProjectRoot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.RelativePath, y.RelativePath, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string ProjectRoot, string RelativePath) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ProjectRoot),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RelativePath));
    }
}
