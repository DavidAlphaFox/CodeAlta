using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins;

/// <summary>
/// Identifies the kind of pending source change for a plugin package.
/// </summary>
public enum PluginSourceChangeKind
{
    /// <summary>A package was added.</summary>
    Added,
    /// <summary>A package source file changed.</summary>
    Changed,
    /// <summary>A package was deleted.</summary>
    Deleted,
    /// <summary>CodeAlta-generated root build files changed.</summary>
    BuildFilesChanged,
    /// <summary>The watcher overflowed or a package could not be determined and a rescan is required.</summary>
    UnknownRescanRequired,
}

/// <summary>
/// Describes a debounced plugin source change notification.
/// </summary>
public sealed record PluginSourceChange
{
    /// <summary>Gets the plugin root.</summary>
    public required PluginRoot Root { get; init; }

    /// <summary>Gets the package id, when known.</summary>
    public string? PackageId { get; init; }

    /// <summary>Gets the changed path, when known.</summary>
    public string? Path { get; init; }

    /// <summary>Gets the change kind.</summary>
    public required PluginSourceChangeKind Kind { get; init; }

    /// <summary>Gets the timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Monitors plugin roots for source changes.
/// </summary>
public interface IPluginChangeMonitor : IAsyncDisposable
{
    /// <summary>Raised after plugin source changes are debounced and coalesced per package.</summary>
    event EventHandler<PluginSourceChange>? Changed;

    /// <summary>Gets pending changes keyed by package id or a root rescan key.</summary>
    IReadOnlyList<PluginSourceChange> PendingChanges { get; }

    /// <summary>Starts monitoring plugin roots.</summary>
    void Start();

    /// <summary>Marks pending changes for a package/root rescan key as processed after a serialized reload/disable operation.</summary>
    /// <param name="packageId">The package id to clear, or <see langword="null"/> to clear root-level rescan changes.</param>
    /// <param name="root">The plugin root used for root-level rescan keys.</param>
    void MarkProcessed(string? packageId, PluginRoot? root = null);

    /// <summary>Clears all pending changes.</summary>
    void ClearPending();
}

/// <summary>
/// Serializes watcher state updates with plugin rebuild, reload, and disable operations.
/// </summary>
public sealed class PluginChangeOperationCoordinator
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Runs an operation under the change-operation lock and marks the corresponding watcher change as processed on success.
    /// </summary>
    /// <param name="monitor">The change monitor.</param>
    /// <param name="packageId">The package id to mark processed, or <see langword="null"/> for a root rescan.</param>
    /// <param name="operation">The operation to run.</param>
    /// <param name="root">The plugin root used for root-level rescan keys.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the operation has run and pending state has been updated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="monitor"/> or <paramref name="operation"/> is <see langword="null"/>.</exception>
    public async Task RunAndMarkProcessedAsync(
        IPluginChangeMonitor monitor,
        string? packageId,
        Func<CancellationToken, Task> operation,
        PluginRoot? root = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(operation);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            monitor.MarkProcessed(packageId, root);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

/// <summary>
/// File-system watcher implementation of <see cref="IPluginChangeMonitor"/>.
/// </summary>
public sealed class FileSystemPluginChangeMonitor : IPluginChangeMonitor
{
    private static readonly string[] RootBuildFileNames =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
    ];

    private readonly IReadOnlyList<PluginRoot> _roots;
    private readonly TimeSpan _debounceDelay;
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, PendingChangeState> _pending = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemPluginChangeMonitor"/> class.
    /// </summary>
    /// <param name="roots">The plugin roots to monitor.</param>
    /// <param name="debounceDelay">The debounce delay. Defaults to 500 milliseconds.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="roots"/> is <see langword="null"/>.</exception>
    public FileSystemPluginChangeMonitor(IEnumerable<PluginRoot> roots, TimeSpan? debounceDelay = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _roots = roots.ToArray();
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(500);
    }

    /// <inheritdoc />
    public event EventHandler<PluginSourceChange>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<PluginSourceChange> PendingChanges
    {
        get
        {
            lock (_gate)
            {
                return _pending.Values.Select(static state => state.Change).ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            foreach (var root in _roots.Where(static root => Directory.Exists(root.RootPath)))
            {
                var watcher = new FileSystemWatcher(root.RootPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName |
                        NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.CreationTime |
                        NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                watcher.Created += (_, args) => Queue(root, args.FullPath, PluginSourceChangeKind.Added);
                watcher.Changed += (_, args) => Queue(root, args.FullPath, PluginSourceChangeKind.Changed);
                watcher.Deleted += (_, args) => Queue(root, args.FullPath, PluginSourceChangeKind.Deleted);
                watcher.Renamed += (_, args) =>
                {
                    Queue(root, args.OldFullPath, PluginSourceChangeKind.Deleted);
                    Queue(root, args.FullPath, PluginSourceChangeKind.Added);
                };
                watcher.Error += (_, _) => Queue(root, root.RootPath, PluginSourceChangeKind.UnknownRescanRequired);
                _watchers.Add(watcher);
            }
        }
    }

    /// <inheritdoc />
    public void MarkProcessed(string? packageId, PluginRoot? root = null)
    {
        var key = packageId ?? (root is null ? null : GetRescanKey(root));
        if (key is null)
        {
            return;
        }

        PendingChangeState? removed = null;
        lock (_gate)
        {
            if (_pending.Remove(key, out removed))
            {
                removed.Timer.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void ClearPending()
    {
        List<Timer> timers;
        lock (_gate)
        {
            timers = _pending.Values.Select(static state => state.Timer).ToList();
            _pending.Clear();
        }

        foreach (var timer in timers)
        {
            timer.Dispose();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        List<FileSystemWatcher> watchers;
        List<Timer> timers;
        lock (_gate)
        {
            watchers = [.. _watchers];
            timers = _pending.Values.Select(static state => state.Timer).ToList();
            _watchers.Clear();
            _pending.Clear();
            _started = false;
        }

        foreach (var watcher in watchers)
        {
            watcher.Dispose();
        }

        foreach (var timer in timers)
        {
            timer.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void Queue(PluginRoot root, string path, PluginSourceChangeKind kind)
    {
        if (IsIgnoredPath(path))
        {
            return;
        }

        var packageId = TryGetPackageId(root, path);
        if (RootBuildFileNames.Contains(System.IO.Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            kind = PluginSourceChangeKind.BuildFilesChanged;
            packageId = null;
        }

        var key = packageId ?? GetRescanKey(root);
        var change = new PluginSourceChange
        {
            Root = root,
            PackageId = packageId,
            Path = path,
            Kind = kind,
        };

        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var existing))
            {
                existing.Change = Coalesce(existing.Change, change);
                existing.Timer.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
                return;
            }

            var state = new PendingChangeState(change);
            state.Timer = new Timer(_ => Publish(key), null, _debounceDelay, Timeout.InfiniteTimeSpan);
            _pending[key] = state;
        }
    }

    private static string GetRescanKey(PluginRoot root)
        => $"{root.RootPath}:rescan";

    private void Publish(string key)
    {
        PluginSourceChange? change = null;
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var state))
            {
                change = state.Change;
            }
        }

        if (change is not null)
        {
            Changed?.Invoke(this, change);
        }
    }

    private static PluginSourceChange Coalesce(PluginSourceChange previous, PluginSourceChange next)
    {
        if (previous.Kind == PluginSourceChangeKind.UnknownRescanRequired || next.Kind == PluginSourceChangeKind.UnknownRescanRequired)
        {
            return next with { Kind = PluginSourceChangeKind.UnknownRescanRequired };
        }

        if (previous.Kind == PluginSourceChangeKind.Deleted || next.Kind == PluginSourceChangeKind.Deleted)
        {
            return next with { Kind = PluginSourceChangeKind.Deleted };
        }

        if (previous.Kind == PluginSourceChangeKind.Added || next.Kind == PluginSourceChangeKind.Added)
        {
            return next with { Kind = PluginSourceChangeKind.Added };
        }

        if (previous.Kind == PluginSourceChangeKind.BuildFilesChanged || next.Kind == PluginSourceChangeKind.BuildFilesChanged)
        {
            return next with { Kind = PluginSourceChangeKind.BuildFilesChanged };
        }

        return next with { Kind = PluginSourceChangeKind.Changed };
    }

    private static string? TryGetPackageId(PluginRoot root, string path)
    {
        var relative = System.IO.Path.GetRelativePath(root.RootPath, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
        {
            return null;
        }

        var separatorIndex = relative.IndexOfAny([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]);
        return separatorIndex <= 0 ? null : relative[..separatorIndex];
    }

    private static bool IsIgnoredPath(string path)
    {
        var segments = path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return segments.Any(static segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, ".codealta", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PendingChangeState
    {
        public PendingChangeState(PluginSourceChange change)
        {
            Change = change;
            Timer = null!;
        }

        public PluginSourceChange Change { get; set; }

        public Timer Timer { get; set; }
    }
}
