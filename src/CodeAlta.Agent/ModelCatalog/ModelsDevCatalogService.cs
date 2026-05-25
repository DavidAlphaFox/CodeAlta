using System.Diagnostics.CodeAnalysis;
using XenoAtom.Logging;

namespace CodeAlta.Agent.ModelCatalog;

/// <summary>
/// Maintains the current models.dev catalog snapshot for local model metadata enrichment.
/// </summary>
public sealed class ModelsDevCatalogService : IAsyncDisposable
{
    /// <summary>
    /// The snapshot content file name shipped with <c>CodeAlta.Agent</c>.
    /// </summary>
    public const string DefaultSnapshotFileName = "Data/models_dev_db.json";

    /// <summary>
    /// The legacy embedded snapshot resource name used by older builds.
    /// </summary>
    public const string DefaultSnapshotResourceName = "CodeAlta.Agent.Data.models_dev_db.json";

    private static readonly Logger Logger = LogManager.GetLogger("CodeAlta.ModelCatalog");

    private readonly object _gate = new();
    private readonly string _snapshotFilePath;
    private readonly string _snapshotResourceName;
    private readonly string? _cacheFilePath;
    private readonly Uri _refreshUri;
    private readonly TimeSpan _refreshInterval;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _backgroundRefreshTask;
    private volatile ModelsDevDatabase _currentDatabase;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelsDevCatalogService"/> class.
    /// </summary>
    /// <param name="options">The catalog options.</param>
    public ModelsDevCatalogService(ModelsDevCatalogServiceOptions? options = null)
        : this(initialDatabase: null, options)
    {
    }

    internal ModelsDevCatalogService(ModelsDevDatabase? initialDatabase, ModelsDevCatalogServiceOptions? options)
    {
        options ??= new ModelsDevCatalogServiceOptions();
        _snapshotFilePath = string.IsNullOrWhiteSpace(options.SnapshotFilePath)
            ? Path.Combine(AppContext.BaseDirectory, DefaultSnapshotFileName)
            : Path.GetFullPath(options.SnapshotFilePath);
        _snapshotResourceName = string.IsNullOrWhiteSpace(options.SnapshotResourceName)
            ? DefaultSnapshotResourceName
            : options.SnapshotResourceName.Trim();
        _cacheFilePath = string.IsNullOrWhiteSpace(options.CacheFilePath)
            ? null
            : Path.GetFullPath(options.CacheFilePath);
        _refreshUri = options.RefreshUri ?? new Uri("https://models.dev/api.json", UriKind.Absolute);
        _refreshInterval = options.RefreshInterval <= TimeSpan.Zero
            ? TimeSpan.FromHours(12)
            : options.RefreshInterval;
        _httpClient = options.HttpClient ?? new HttpClient();
        _ownsHttpClient = options.HttpClient is null;
        _currentDatabase = initialDatabase ?? LoadInitialDatabase();
    }

    /// <summary>
    /// Gets the current in-memory database snapshot.
    /// </summary>
    public ModelsDevDatabase CurrentDatabase => _currentDatabase;

    /// <summary>
    /// Starts the non-blocking background refresh loop.
    /// </summary>
    public void StartBackgroundRefresh()
    {
        lock (_gate)
        {
            if (_backgroundRefreshTask is not null)
            {
                return;
            }

            _backgroundRefreshTask = Task.Run(() => RefreshLoopAsync(_disposeCts.Token));
        }
    }

    /// <summary>
    /// Tries to resolve a provider by models.dev provider identifier.
    /// </summary>
    /// <param name="providerId">The models.dev provider identifier.</param>
    /// <param name="provider">The resolved provider.</param>
    /// <returns><see langword="true"/> when found; otherwise <see langword="false"/>.</returns>
    public bool TryGetProvider(string providerId, [NotNullWhen(true)] out ModelsDevProviderDefinition? provider)
        => _currentDatabase.TryGetProvider(providerId, out provider);

    /// <summary>
    /// Tries to resolve a model by models.dev provider identifier and model identifier.
    /// </summary>
    /// <param name="providerId">The models.dev provider identifier.</param>
    /// <param name="modelId">The model identifier.</param>
    /// <param name="model">The resolved model.</param>
    /// <returns><see langword="true"/> when found; otherwise <see langword="false"/>.</returns>
    public bool TryGetModel(
        string providerId,
        string modelId,
        [NotNullWhen(true)] out ModelsDevModelDefinition? model)
        => _currentDatabase.TryGetModel(providerId, modelId, out model);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();

        Task? refreshTask;
        lock (_gate)
        {
            refreshTask = _backgroundRefreshTask;
        }

        if (refreshTask is not null)
        {
            try
            {
                await refreshTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _disposeCts.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private ModelsDevDatabase LoadInitialDatabase()
    {
        var snapshot = LoadSnapshotDatabase();
        var cached = TryLoadCachedDatabase();
        return cached ?? snapshot;
    }

    private ModelsDevDatabase LoadSnapshotDatabase()
    {
        if (File.Exists(_snapshotFilePath))
        {
            try
            {
                using var stream = File.OpenRead(_snapshotFilePath);
                return ModelsDevDatabaseJson.Deserialize(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Logger.Warn($"Failed to load models.dev snapshot content file '{_snapshotFilePath}': {ex.Message}");
            }
        }

        return LoadEmbeddedDatabaseFallback();
    }

    private ModelsDevDatabase LoadEmbeddedDatabaseFallback()
    {
        var assembly = typeof(ModelsDevCatalogService).Assembly;
        using var stream = assembly.GetManifestResourceStream(_snapshotResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The models.dev snapshot content file '{_snapshotFilePath}' and legacy embedded resource '{_snapshotResourceName}' were not found.");
        }

        return ModelsDevDatabaseJson.Deserialize(stream);
    }

    private ModelsDevDatabase? TryLoadCachedDatabase()
    {
        if (string.IsNullOrWhiteSpace(_cacheFilePath) || !File.Exists(_cacheFilePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_cacheFilePath);
            return ModelsDevDatabaseJson.Deserialize(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Logger.Warn($"Failed to load cached models.dev database '{_cacheFilePath}': {ex.Message}");
            return null;
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_refreshInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await _httpClient.GetStreamAsync(_refreshUri, cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            memory.Position = 0;

            var database = ModelsDevDatabaseJson.Deserialize(memory);
            _currentDatabase = database;
            await PersistCacheAsync(database, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Logger.Warn($"Failed to refresh models.dev catalog from '{_refreshUri.ToString()}': {ex.Message}");
        }
    }

    private async Task PersistCacheAsync(ModelsDevDatabase database, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_cacheFilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = ModelsDevDatabaseJson.SerializeUtf8(database);
        var tempPath = $"{_cacheFilePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

        try
        {
            File.Move(tempPath, _cacheFilePath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }

            throw;
        }
    }
}

/// <summary>
/// Describes how the models.dev catalog service loads and refreshes snapshots.
/// </summary>
public sealed class ModelsDevCatalogServiceOptions
{
    /// <summary>
    /// Gets or sets the snapshot content-file path.
    /// </summary>
    public string? SnapshotFilePath { get; init; }

    /// <summary>
    /// Gets or sets the embedded snapshot resource name.
    /// </summary>
    public string? SnapshotResourceName { get; init; }

    /// <summary>
    /// Gets or sets the optional cache-file path.
    /// </summary>
    public string? CacheFilePath { get; init; }

    /// <summary>
    /// Gets or sets the refresh endpoint.
    /// </summary>
    public Uri? RefreshUri { get; init; }

    /// <summary>
    /// Gets or sets the refresh interval.
    /// </summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Gets or sets the HTTP client used for background refreshes.
    /// </summary>
    public HttpClient? HttpClient { get; init; }
}
