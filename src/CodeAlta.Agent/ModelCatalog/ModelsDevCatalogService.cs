using System.Diagnostics.CodeAnalysis;
using XenoAtom.Logging;

namespace CodeAlta.Agent.ModelCatalog;

// 模块功能：维护 models.dev 目录快照，支持后台定时刷新与本地缓存，为模型元数据提供查询接口
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

    // 说明：模型目录日志记录器
    private static readonly Logger Logger = LogManager.GetLogger("CodeAlta.ModelCatalog");

    // 说明：保护后台刷新任务字段的锁
    private readonly object _gate = new();
    // 说明：随程序发布的快照文件路径
    private readonly string _snapshotFilePath;
    // 说明：内嵌程序集资源名称（旧版兜底）
    private readonly string _snapshotResourceName;
    // 说明：可选的本地缓存文件路径，为 null 时禁用持久化缓存
    private readonly string? _cacheFilePath;
    // 说明：远端刷新 API 地址
    private readonly Uri _refreshUri;
    // 说明：后台刷新间隔，默认 12 小时
    private readonly TimeSpan _refreshInterval;
    // 说明：用于下载最新目录的 HTTP 客户端
    private readonly HttpClient _httpClient;
    // 说明：是否由本类自行管理 HttpClient 生命周期
    private readonly bool _ownsHttpClient;
    // 说明：用于在 DisposeAsync 时取消后台刷新循环
    private readonly CancellationTokenSource _disposeCts = new();
    // 说明：后台刷新任务引用，null 表示尚未启动
    private Task? _backgroundRefreshTask;
    // 说明：当前内存中的数据库快照，volatile 保证读可见性
    private volatile ModelsDevDatabase _currentDatabase;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelsDevCatalogService"/> class.
    /// </summary>
    /// <param name="options">The catalog options.</param>
    public ModelsDevCatalogService(ModelsDevCatalogServiceOptions? options = null)
        : this(initialDatabase: null, options)
    {
    }

    // 函数功能：内部构造，支持注入初始数据库快照（主要用于测试），并根据选项初始化所有字段
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

    // 函数功能：加载初始数据库，优先使用本地缓存，无缓存时回退到快照文件
    private ModelsDevDatabase LoadInitialDatabase()
    {
        var snapshot = LoadSnapshotDatabase();
        var cached = TryLoadCachedDatabase();
        return cached ?? snapshot;
    }

    // 函数功能：从快照文件加载数据库，文件不存在或异常时回退到内嵌程序集资源
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

    // 函数功能：从内嵌程序集资源加载数据库，资源缺失时抛出 InvalidOperationException
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

    // 函数功能：尝试从本地缓存文件加载数据库，缓存不存在或解析失败时返回 null
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

    // 函数功能：后台刷新循环，立即执行一次后按 _refreshInterval 定时触发
    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_refreshInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // 函数功能：从远端拉取最新目录并更新内存快照，成功后持久化缓存；HTTP/解析异常仅记录警告
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

    // 函数功能：将数据库序列化后原子写入缓存文件（先写临时文件再 Move 替换）
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

// 类型：models.dev 目录服务的配置选项，控制快照路径、缓存路径、刷新地址和间隔等
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
