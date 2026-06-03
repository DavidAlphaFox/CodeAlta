using CodeAlta.Agent.ModelCatalog;
using XenoAtom.Logging;

namespace CodeAlta.Agent.Runtime;

// 模块功能：CodeAlta 自有会话运行时，负责管理多 provider 注册、模型目录缓存、会话创建/恢复/删除及生命周期控制。
/// <summary>
/// CodeAlta-owned session runtime for provider-backed raw-API sessions.
/// </summary>
public sealed class AgentRuntime : IAsyncDisposable
{
    private static readonly Logger Logger = LogManager.GetLogger("CodeAlta.Agent.Runtime");
    private readonly AgentRuntimeOptions _options;
    private readonly object _storeLock = new();
    private readonly AgentRuntimePathLayout _layout;
    private readonly IReadOnlyDictionary<string, AgentRuntimeProviderRegistration> _providersByKey;
    private readonly Dictionary<string, IReadOnlyList<AgentModelInfo>> _modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly AgentSessionJournalFile _journalFile = new();
    private IAgentSessionJournalStore? _store;
    private bool _started;

    // 函数功能：构造函数，验证参数合法性，初始化 provider 映射表和文件系统路径布局；providerId 用于持久化字段，displayName 为用户界面名称。
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRuntime"/> class.
    /// </summary>
    /// <param name="providerId">The provider identifier persisted in legacy backend-id fields.</param>
    /// <param name="displayName">The user-facing runtime name.</param>
    /// <param name="options">Runtime options.</param>
    public AgentRuntime(
        ModelProviderId providerId,
        string displayName,
        AgentRuntimeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Providers is not { Count: > 0 })
        {
            throw new ArgumentException("At least one provider registration is required.", nameof(options));
        }

        ProviderId = new ModelProviderId(providerId.Value);
        DisplayName = displayName.Trim();
        _options = options;
        var stateRootPath = string.IsNullOrWhiteSpace(options.StateRootPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".alta")
            : options.StateRootPath;
        _layout = new AgentRuntimePathLayout(stateRootPath);
        _providersByKey = options.Providers.ToDictionary(
            static provider => provider.Provider.ProviderKey,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the provider identifier as stored in legacy persisted backend-id fields.
    /// </summary>
    public ModelProviderId ProviderId { get; }

    /// <summary>
    /// Gets the user-facing runtime name.
    /// </summary>
    public string DisplayName { get; }

    private IAgentSessionJournalStore Store
    {
        get
        {
            if (_store is not null)
            {
                return _store;
            }

            lock (_storeLock)
            {
                return _store ??= new FileSystemAgentSessionStore(
                    _layout,
                    _journalFile);
            }
        }
    }

    // 函数功能：启动运行时（幂等），标记已启动状态；若已启动则直接返回。
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _started = true;
        return Task.CompletedTask;
    }

    // 函数功能：停止运行时，将已启动标记重置为 false。
    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _started = false;
        return Task.CompletedTask;
    }

    // 函数功能：遍历所有 provider 获取模型列表，按 ID 去重并按显示名排序后返回；结果同时写入 _modelCache 供后续使用。
    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<AgentModelInfo>();
        foreach (var provider in _options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogInfo(
                $"Listing models runtimeProviderId={ProviderId.Value} provider={provider.Provider.ProviderKey} displayName={provider.Provider.DisplayName} protocol={provider.Provider.ProtocolFamily} baseUri={FormatUri(provider.Provider.BaseUri)}");

            IReadOnlyList<AgentModelInfo> models;
            try
            {
                var modelCatalog = ResolveModelCatalog(provider);
                if (modelCatalog is null)
                {
                    continue;
                }

                models = await modelCatalog.ListModelsAsync(provider.Provider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogWarn(
                    ex,
                    $"Failed to list models runtimeProviderId={ProviderId.Value} provider={provider.Provider.ProviderKey} displayName={provider.Provider.DisplayName} protocol={provider.Provider.ProtocolFamily} baseUri={FormatUri(provider.Provider.BaseUri)}");
                throw;
            }

            LogInfo(
                $"Listed models runtimeProviderId={ProviderId.Value} provider={provider.Provider.ProviderKey} displayName={provider.Provider.DisplayName} count={models.Count}");
            _modelCache[provider.Provider.ProviderKey] = models;
            results.AddRange(models);
        }

        var mergedModels = results
            .GroupBy(static model => model.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static model => model.DisplayName ?? model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        LogInfo($"Provider model catalog ready runtimeProviderId={ProviderId.Value} providers={_options.Providers.Count} models={mergedModels.Length}");
        return mergedModels;
    }

    // 函数功能：按会话 ID 删除持久化会话，返回是否找到并删除成功。
    /// <inheritdoc />
    public async Task<bool> DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await StartAsync(cancellationToken).ConfigureAwait(false);

        return await Store.DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    // 函数功能：创建新会话，解析目标 provider，生成 session ID 并持久化 summary 与 state，返回可交互的 IAgentSession。
    /// <inheritdoc />
    public async Task<IAgentSession> CreateSessionAsync(
        AgentSessionCreateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await StartAsync(cancellationToken).ConfigureAwait(false);

        var registration = ResolveProvider(options.ProviderKey);
        var now = DateTimeOffset.UtcNow;
        var sessionId = string.IsNullOrWhiteSpace(options.SessionId)
            ? Guid.CreateVersion7().ToString()
            : options.SessionId.Trim();
        var summary = new AgentSessionSummary
        {
            SessionId = sessionId,
            ProviderId = ProviderId,
            ProtocolFamily = registration.Provider.ProtocolFamily,
            ProviderKey = registration.Provider.ProviderKey,
            ModelId = options.Model,
            WorkingDirectory = options.WorkingDirectory,
            Title = NormalizeOptionalText(options.Title),
            Summary = null,
            ParentSessionId = NormalizeOptionalText(options.ParentSessionId),
            CreatedBySessionId = NormalizeOptionalText(options.CreatedBySessionId ?? options.ParentSessionId),
            CreatedByRunId = options.CreatedByRunId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var state = new AgentSessionState
        {
            SessionId = sessionId,
            ProtocolFamily = registration.Provider.ProtocolFamily,
            ProviderKey = registration.Provider.ProviderKey,
            UpdatedAt = now,
        };

        await Store.UpsertSessionAsync(summary, cancellationToken).ConfigureAwait(false);
        await Store.UpsertStateAsync(state, cancellationToken).ConfigureAwait(false);
        return new AgentSession(
            ProviderId,
            registration.Provider,
            summary,
            state,
            [],
            Store,
            registration.TurnExecutor,
            options,
            allowProviderContinuation: true,
            cachedModels: GetCachedModels(registration));
    }

    // 函数功能：恢复已有会话，从 store 读取 summary/state/历史事件，必要时迁移到新 provider 并修复用量数据，返回可继续对话的 IAgentSession。
    /// <inheritdoc />
    public async Task<IAgentSession> ResumeSessionAsync(
        string sessionId,
        AgentSessionResumeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(options);
        await StartAsync(cancellationToken).ConfigureAwait(false);

        var summary = await Store.GetSessionSummaryAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (summary is null)
        {
            throw new KeyNotFoundException($"The session '{sessionId}' was not found for runtime '{ProviderId.Value}'.");
        }

        var provider = ResolveResumeProvider(options, summary);
        var state = await Store.GetStateAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? new AgentSessionState
            {
                SessionId = sessionId,
                ProtocolFamily = summary.ProtocolFamily,
                ProviderKey = summary.ProviderKey,
                UpdatedAt = summary.UpdatedAt,
            };
        var history = await Store.ReadEventsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (!MatchesProvider(summary, provider.Provider) || !MatchesProvider(state, provider.Provider))
        {
            var now = DateTimeOffset.UtcNow;
            summary = TransferSummaryToProvider(summary, provider.Provider, options, now);
            state = TransferStateToProvider(state, provider.Provider, now);
            await Store.UpsertSessionAsync(summary, cancellationToken).ConfigureAwait(false);
            await Store.UpsertStateAsync(state, cancellationToken).ConfigureAwait(false);
        }

        (summary, state) = await RepairRecoveredUsageAsync(summary, state, history, provider, options, cancellationToken).ConfigureAwait(false);

        return new AgentSession(
            ProviderId,
            provider.Provider,
            OverrideSummary(summary, options),
            state,
            history,
            Store,
            provider.TurnExecutor,
            options,
            allowProviderContinuation: false,
            cachedModels: GetCachedModels(provider));

    }

    // 函数功能：释放所有已注册 provider 的 TurnExecutor，支持 IAsyncDisposable 和 IDisposable 两种接口。
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var registration in _options.Providers)
        {
            switch (registration.TurnExecutor)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    // 函数功能：按 providerKey 查找已注册的 provider；为空时选默认或唯一 provider，否则抛异常。
    private AgentRuntimeProviderRegistration ResolveProvider(string? providerKey)
    {
        if (!string.IsNullOrWhiteSpace(providerKey))
        {
            if (_providersByKey.TryGetValue(providerKey.Trim(), out var resolved))
            {
                return resolved;
            }

            throw new KeyNotFoundException($"The provider '{providerKey}' is not registered for provider runtime '{ProviderId.Value}'.");
        }

        var preferred = _options.Providers.FirstOrDefault(static provider => provider.Provider.IsDefault)
            ?? (_options.Providers.Count == 1 ? _options.Providers[0] : null);
        if (preferred is null)
        {
            throw new InvalidOperationException(
                $"Provider runtime '{ProviderId.Value}' requires an explicit provider key because no single default provider is configured.");
        }

        return preferred;
    }

    // 函数功能：恢复会话时选择 provider：优先用 options.ProviderKey，其次沿用 summary 中记录的上次 provider，最后回退到默认。
    private AgentRuntimeProviderRegistration ResolveResumeProvider(
        AgentSessionResumeOptions options,
        AgentSessionSummary summary)
    {
        if (!string.IsNullOrWhiteSpace(options.ProviderKey))
        {
            return ResolveProvider(options.ProviderKey);
        }

        if (!string.IsNullOrWhiteSpace(summary.ProviderKey) &&
            _providersByKey.TryGetValue(summary.ProviderKey, out var lastProvider))
        {
            return lastProvider;
        }

        return ResolveProvider(null);
    }

    // 函数功能：将 summary 迁移至新 provider，更新协议族、providerKey、模型 ID、工作目录等字段，返回新 record 实例。
    private AgentSessionSummary TransferSummaryToProvider(
        AgentSessionSummary summary,
        ModelProviderRuntimeDescriptor provider,
        AgentSessionResumeOptions options,
        DateTimeOffset updatedAt)
        => summary with
        {
            ProviderId = ProviderId,
            ProtocolFamily = provider.ProtocolFamily,
            ProviderKey = provider.ProviderKey,
            ModelId = NormalizeOptionalText(options.Model),
            WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory) ? summary.WorkingDirectory : options.WorkingDirectory,
            Title = string.IsNullOrWhiteSpace(options.Title) ? summary.Title : options.Title.Trim(),
            UpdatedAt = updatedAt,
        };

    // 函数功能：将 state 迁移至新 provider，清除旧 provider 会话 ID 和状态，返回新 record 实例。
    private static AgentSessionState TransferStateToProvider(
        AgentSessionState state,
        ModelProviderRuntimeDescriptor provider,
        DateTimeOffset updatedAt)
        => state with
        {
            ProtocolFamily = provider.ProtocolFamily,
            ProviderKey = provider.ProviderKey,
            ProviderSessionId = null,
            ProviderState = null,
            UpdatedAt = updatedAt,
        };

    // 函数功能：用 options 中非空的模型 ID、工作目录、标题覆盖 summary 对应字段，其余保持原值。
    private static AgentSessionSummary OverrideSummary(
        AgentSessionSummary summary,
        AgentSessionResumeOptions options)
    {
        return summary with
        {
            ModelId = string.IsNullOrWhiteSpace(options.Model) ? summary.ModelId : options.Model,
            WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory) ? summary.WorkingDirectory : options.WorkingDirectory,
            Title = string.IsNullOrWhiteSpace(options.Title) ? summary.Title : options.Title.Trim(),
        };
    }

    // 函数功能：将空白字符串规范化为 null，非空字符串修剪首尾空格后返回。
    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // 函数功能：从历史事件重建用量数据，与 summary/state 中已有数据比较后择优保留，并附加模型信息；如有变更则持久化，返回修复后的 summary 和 state。
    private async Task<(AgentSessionSummary Summary, AgentSessionState State)> RepairRecoveredUsageAsync(
        AgentSessionSummary summary,
        AgentSessionState state,
        IReadOnlyList<AgentEvent> history,
        AgentRuntimeProviderRegistration provider,
        AgentSessionResumeOptions options,
        CancellationToken cancellationToken)
    {
        var originalSummaryUsage = summary.Usage;
        var originalStateUsage = state.Usage;
        var recoveredUsage = AgentUsageFactory.RecoverUsageFromHistory(history);
        if (ShouldPreferRecoveredUsage(recoveredUsage, summary.Usage))
        {
            summary = summary with { Usage = recoveredUsage };
        }

        if (ShouldPreferRecoveredUsage(recoveredUsage, state.Usage))
        {
            state = state with { Usage = recoveredUsage };
        }

        var effectiveModelId = options.Model ??
                               summary.ModelId ??
                               state.Usage?.LastOperation?.Model ??
                               summary.Usage?.LastOperation?.Model;
        if (!string.IsNullOrWhiteSpace(effectiveModelId))
        {
            try
            {
                var models = GetCachedModels(provider);
                if (models.Count > 0)
                {
                    var modelInfo = AgentModelIdentity.FindBestMatch(models, effectiveModelId);
                    if (modelInfo is not null)
                    {
                        summary = summary with { Usage = AgentUsageFactory.AttachModelInfo(summary.Usage, modelInfo) };
                        state = state with { Usage = AgentUsageFactory.AttachModelInfo(state.Usage, modelInfo) };
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        var summaryChanged = !Equals(summary.Usage, originalSummaryUsage);
        var stateChanged = !Equals(state.Usage, originalStateUsage);
        if (summaryChanged)
        {
            await Store.UpsertSessionAsync(summary, cancellationToken).ConfigureAwait(false);
        }

        if (stateChanged)
        {
            await Store.UpsertStateAsync(state, cancellationToken).ConfigureAwait(false);
        }

        return (summary, state);
    }

    // 函数功能：判断从历史恢复的用量是否应优先于当前记录的用量；比较时间戳，或在当前缺少 Window/LastOperation 等字段时优先恢复值。
    private static bool ShouldPreferRecoveredUsage(AgentSessionUsage? recovered, AgentSessionUsage? current)
    {
        if (recovered is null)
        {
            return false;
        }

        if (current is null)
        {
            return true;
        }

        if (recovered.UpdatedAt != default && current.UpdatedAt != default)
        {
            if (recovered.UpdatedAt > current.UpdatedAt)
            {
                return true;
            }

            if (recovered.UpdatedAt < current.UpdatedAt)
            {
                return false;
            }
        }

        return (current.Window is null && recovered.Window is not null) ||
               (current.LastOperation is null && recovered.LastOperation is not null) ||
               (current.RateLimits is null && recovered.RateLimits is not null) ||
               (current.CurrentTokens is null && recovered.CurrentTokens is not null) ||
               (current.TokenLimit is null && recovered.TokenLimit is not null);
    }

    // 函数功能：优先从初始化服务的当前状态获取该 provider 的模型列表，命中则刷新缓存；否则返回 _modelCache 中已缓存的列表。
    private IReadOnlyList<AgentModelInfo> GetCachedModels(AgentRuntimeProviderRegistration provider)
    {
        var providerKey = provider.Provider.ProviderKey;
        var state = _options.ModelProviderInitializationService?.CurrentStates.FirstOrDefault(
            state => string.Equals(state.ProviderId.Value, providerKey, StringComparison.OrdinalIgnoreCase));
        if (state?.Models is { Count: > 0 } models)
        {
            _modelCache[providerKey] = models;
            return models;
        }

        return _modelCache.TryGetValue(providerKey, out var cached) ? cached : [];
    }

    // 函数功能：取 provider 的 ModelCatalog，若未显式设置则尝试将 TurnExecutor 转型为 IModelProviderModelCatalog 使用。
    private static IModelProviderModelCatalog? ResolveModelCatalog(AgentRuntimeProviderRegistration provider)
        => provider.ModelCatalog ?? provider.TurnExecutor as IModelProviderModelCatalog;

    // 函数功能：检查 summary 的协议族和 providerKey 是否与目标 provider 一致（不区分大小写）。
    private static bool MatchesProvider(AgentSessionSummary summary, ModelProviderRuntimeDescriptor provider)
        => string.Equals(summary.ProtocolFamily, provider.ProtocolFamily, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(summary.ProviderKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase);

    // 函数功能：检查 state 的协议族和 providerKey 是否与目标 provider 一致（不区分大小写）。
    private static bool MatchesProvider(AgentSessionState state, ModelProviderRuntimeDescriptor provider)
        => string.Equals(state.ProtocolFamily, provider.ProtocolFamily, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(state.ProviderKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase);

    // 函数功能：将 Uri 格式化为字符串，null 时返回占位符 "<default>"。
    private static string FormatUri(Uri? uri)
        => uri?.ToString() ?? "<default>";

    // 函数功能：以 Info 级别写入结构化日志消息。
    private static void LogInfo(string message)
    {
        Logger.Info(message);
    }

    // 函数功能：以 Warn 级别写入结构化日志消息，附带异常信息。
    private static void LogWarn(Exception exception, string message)
    {
        Logger.Warn(exception, message);
    }
}
