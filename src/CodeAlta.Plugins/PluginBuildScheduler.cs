namespace CodeAlta.Plugins;

/// <summary>
/// Describes plugin build scheduler options.
/// </summary>
public sealed record PluginBuildSchedulerOptions
{
    /// <summary>Gets the maximum number of concurrent plugin builds.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Math.Min(Environment.ProcessorCount, 4);
}

/// <summary>
/// Identifies a plugin build scheduler progress state.
/// </summary>
public enum PluginBuildProgressState
{
    /// <summary>The build has been queued.</summary>
    Queued,
    /// <summary>The build is running.</summary>
    Running,
    /// <summary>The build completed successfully.</summary>
    Succeeded,
    /// <summary>The build failed.</summary>
    Failed,
    /// <summary>The build was skipped because it was up to date.</summary>
    UpToDate,
}

/// <summary>
/// Describes plugin build scheduler progress for interactive and headless hosts.
/// </summary>
public sealed record PluginBuildProgress
{
    /// <summary>Gets the source plugin package.</summary>
    public required SourcePluginPackage Package { get; init; }

    /// <summary>Gets the request ordinal.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the total request count.</summary>
    public required int Total { get; init; }

    /// <summary>Gets the progress state.</summary>
    public required PluginBuildProgressState State { get; init; }

    /// <summary>Gets the timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Schedules source plugin builds with bounded parallelism and failure isolation.
/// </summary>
public sealed class PluginBuildScheduler
{
    private readonly IPluginBuildService _buildService;
    private readonly PluginBuildSchedulerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginBuildScheduler"/> class.
    /// </summary>
    /// <param name="buildService">The build service.</param>
    /// <param name="options">Scheduler options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buildService"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when max degree of parallelism is less than one.</exception>
    public PluginBuildScheduler(IPluginBuildService buildService, PluginBuildSchedulerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(buildService);
        _buildService = buildService;
        _options = options ?? new PluginBuildSchedulerOptions();
        if (_options.MaxDegreeOfParallelism < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Max degree of parallelism must be at least one.");
        }
    }

    /// <summary>Raised when a package transitions through scheduler progress states.</summary>
    public event EventHandler<PluginBuildProgress>? ProgressChanged;

    /// <summary>
    /// Builds plugin packages with bounded parallelism.
    /// </summary>
    /// <param name="requests">The build requests.</param>
    /// <param name="cancellationToken">A token to cancel all builds.</param>
    /// <returns>Build results in request order.</returns>
    public async ValueTask<IReadOnlyList<PluginBuildResult>> BuildAsync(
        IReadOnlyList<PluginBuildRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var results = new PluginBuildResult?[requests.Count];
        for (var index = 0; index < requests.Count; index++)
        {
            OnProgress(requests[index].Package, index, requests.Count, PluginBuildProgressState.Queued);
        }

        await Parallel.ForEachAsync(
            Enumerable.Range(0, requests.Count),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
            },
            async (index, token) =>
            {
                try
                {
                    OnProgress(requests[index].Package, index, requests.Count, PluginBuildProgressState.Running);
                    results[index] = await _buildService.BuildAsync(requests[index], token).ConfigureAwait(false);
                    OnProgress(requests[index].Package, index, requests.Count, results[index]!.IsUpToDate
                        ? PluginBuildProgressState.UpToDate
                        : results[index]!.Succeeded ? PluginBuildProgressState.Succeeded : PluginBuildProgressState.Failed);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var package = requests[index].Package;
                    results[index] = new PluginBuildResult
                    {
                        Package = package,
                        Succeeded = false,
                        RuntimeDiagnostics =
                        [
                            PluginRuntimeDiagnostic.Error(
                                PluginRuntimeDiagnosticSource.Build,
                                $"Plugin build failed before MSBuild completed: {ex.Message}",
                                package.PackageId,
                                package.EntryFilePath,
                                ex),
                        ],
                    };
                    OnProgress(package, index, requests.Count, PluginBuildProgressState.Failed);
                }
            }).ConfigureAwait(false);

        return results.Select(static result => result!).ToArray();
    }

    private void OnProgress(SourcePluginPackage package, int index, int total, PluginBuildProgressState state)
        => ProgressChanged?.Invoke(this, new PluginBuildProgress
        {
            Package = package,
            Index = index,
            Total = total,
            State = state,
        });
}
