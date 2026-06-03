using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace CodeAlta.Agent.Runtime;

// 模块功能：将 Agent 会话日志以 JSONL 文件形式持久化到本地文件系统，实现 IAgentSessionJournalStore 接口
/// <summary>
/// Persists local raw-API session journals on the filesystem.
/// </summary>
public sealed class FileSystemAgentSessionStore : IAgentSessionJournalStore
{
    private const string SessionSummaryEventType = "local.sessionSummary";
    private const string SessionStateEventType = "local.sessionState";
    private const string CodeAltaSessionHeaderEventType = "codealta.sessionHeader";
    private const string CodeAltaSessionStateEventType = "codealta.sessionState";
    private const int MetadataProbeHeadByteCount = 64 * 1024;
    private const int MetadataProbeTailByteCount = 256 * 1024;
    private const int DefaultMaxConcurrentMetadataProjections = 8;

    private static readonly TimeSpan ReadRetryTime = TimeSpan.FromMilliseconds(250);
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly AgentRuntimePathLayout _layout;
    private readonly AgentSessionJournalFile _journalFile;
    private readonly int _maxConcurrentMetadataProjections;
    private readonly ConcurrentDictionary<string, CachedSessionProjection> _metadataProjectionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _sessionFiles = new(StringComparer.OrdinalIgnoreCase);

    // 函数功能：公开构造函数，接受文件系统布局对象，使用默认并发数初始化存储
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemAgentSessionStore"/> class.
    /// </summary>
    /// <param name="layout">Filesystem layout.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="layout"/> is <see langword="null" />.</exception>
    public FileSystemAgentSessionStore(AgentRuntimePathLayout layout)
        : this(layout, new AgentSessionJournalFile())
    {
    }

    // 函数功能：内部构造函数，接受布局与日志文件对象，使用默认并发数
    internal FileSystemAgentSessionStore(AgentRuntimePathLayout layout, AgentSessionJournalFile journalFile)
        : this(layout, journalFile, DefaultMaxConcurrentMetadataProjections)
    {
    }

    // 函数功能：内部完整构造函数，接受布局、日志文件对象及最大并发投影数，完成所有字段初始化
    internal FileSystemAgentSessionStore(
        AgentRuntimePathLayout layout,
        AgentSessionJournalFile journalFile,
        int maxConcurrentMetadataProjections)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(journalFile);
        if (maxConcurrentMetadataProjections < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentMetadataProjections), "Concurrency must be greater than zero.");
        }

        _layout = layout;
        _journalFile = journalFile;
        _maxConcurrentMetadataProjections = maxConcurrentMetadataProjections;
    }

    // 函数功能：新建或更新会话摘要，将 SessionSummary 事件追加写入对应的 JSONL 文件
    /// <inheritdoc />
    public async Task UpsertSessionAsync(
        AgentSessionSummary session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var sessionFile = await GetOrCreateSessionFilePathAsync(
            session.SessionId,
            session.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        var snapshotEvent = new AgentRawEvent(
            session.ProviderId,
            session.SessionId,
            session.UpdatedAt,
            SessionSummaryEventType,
            JsonSerializer.SerializeToElement(session, AgentJsonSerializerContext.Default.AgentSessionSummary),
            null);

        await AppendLinesAsync(sessionFile, [snapshotEvent.ToJson()], cancellationToken).ConfigureAwait(false);
    }

    // 函数功能：按协议族+提供方+会话ID查找会话摘要，不匹配 scope 时返回 null
    /// <inheritdoc />
    public async Task<AgentSessionSummary?> GetSessionAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var projection = await TryProjectSessionAsync(sessionId, includeHistory: false, cancellationToken).ConfigureAwait(false);
        if (projection is null || projection.Summary is null)
        {
            return null;
        }

        return MatchesScope(projection.Summary, protocolFamily, providerKey)
            ? projection.Summary
            : null;
    }

    // 函数功能：按会话ID获取会话元数据（含摘要与状态），会话不存在时返回 null
    /// <inheritdoc />
    public async Task<AgentSessionMetadata?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var projection = await TryProjectSessionAsync(sessionId, includeHistory: false, cancellationToken).ConfigureAwait(false);
        return projection?.Summary is null
            ? null
            : ToMetadata(projection.Summary, projection.State);
    }

    // 函数功能：按会话ID获取会话摘要，不含历史事件；不存在时返回 null
    /// <inheritdoc />
    public async Task<AgentSessionSummary?> GetSessionSummaryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var projection = await TryProjectSessionAsync(sessionId, includeHistory: false, cancellationToken).ConfigureAwait(false);
        return projection?.Summary;
    }

    // 函数功能：按协议族+提供方筛选并异步枚举所有会话摘要
    /// <inheritdoc />
    public async IAsyncEnumerable<AgentSessionSummary> ListSessionSummariesAsync(
        string protocolFamily,
        string providerKey,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var session in ListSessionSummariesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (MatchesScope(session, protocolFamily, providerKey))
            {
                yield return session;
            }
        }
    }

    // 函数功能：按可选过滤器异步枚举所有会话元数据（含上下文/路径等信息）
    /// <inheritdoc />
    public async IAsyncEnumerable<AgentSessionMetadata> ListSessionsAsync(
        AgentSessionListFilter? filter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var projection in ListSessionProjectionsAsync(cancellationToken).ConfigureAwait(false))
        {
            var metadata = ToMetadata(projection.Projection.Summary!, projection.Projection.State);
            if (MatchesFilter(metadata, filter))
            {
                yield return metadata;
            }
        }
    }

    // 函数功能：异步枚举所有会话摘要（无 scope 过滤），内部委托给 ListSessionProjectionsAsync
    /// <inheritdoc />
    public async IAsyncEnumerable<AgentSessionSummary> ListSessionSummariesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var projection in ListSessionProjectionsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return projection.Projection.Summary!;
        }
    }

    // 函数功能：扫描会话目录下所有 .jsonl 文件，并发投影元数据，按最后写入时间降序异步逐条产出
    private async IAsyncEnumerable<ListedSessionProjection> ListSessionProjectionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_layout.SessionsRootPath))
        {
            yield break;
        }

        var sessionFiles = Directory
            .EnumerateFiles(_layout.SessionsRootPath, "*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(static sessionFile => File.GetLastWriteTimeUtc(sessionFile))
            .ThenByDescending(static sessionFile => sessionFile, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var offset = 0; offset < sessionFiles.Length; offset += _maxConcurrentMetadataProjections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(_maxConcurrentMetadataProjections, sessionFiles.Length - offset);
            var tasks = new Task<ListedSessionProjection?>[count];
            for (var index = 0; index < count; index++)
            {
                tasks[index] = ProjectSessionFileForListingAsync(sessionFiles[offset + index], cancellationToken);
            }

            foreach (var task in tasks)
            {
                var listedProjection = await task.ConfigureAwait(false);
                if (listedProjection?.Projection.Summary is null)
                {
                    continue;
                }

                _sessionFiles[listedProjection.Projection.Summary.SessionId] = listedProjection.SessionFile;
                yield return listedProjection;
            }
        }
    }

    // 函数功能：对单个 JSONL 文件进行元数据投影，忽略 IO/JSON 异常，返回 null 表示跳过该文件
    private async Task<ListedSessionProjection?> ProjectSessionFileForListingAsync(
        string sessionFile,
        CancellationToken cancellationToken)
    {
        try
        {
            var projection = await ProjectSessionFileAsync(sessionFile, includeHistory: false, cancellationToken).ConfigureAwait(false);
            return projection.Summary is null
                ? null
                : new ListedSessionProjection(sessionFile, projection);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 函数功能：将一批事件以 JSON 行形式追加写入指定会话的 JSONL 文件
    /// <inheritdoc />
    public async Task AppendEventsAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        IReadOnlyList<AgentEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return;
        }

        var sessionFile = await GetExistingSessionFilePathAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await AppendLinesAsync(
                sessionFile,
                events.Select(static @event => @event.ToJson()).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：读取指定 scope 下会话的完整事件历史，scope 不匹配或会话不存在则返回空列表
    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentEvent>> ReadEventsAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var projection = await TryProjectSessionAsync(sessionId, includeHistory: true, cancellationToken).ConfigureAwait(false);
        if (projection is null || projection.Summary is null || !MatchesScope(projection.Summary, protocolFamily, providerKey))
        {
            return [];
        }

        return projection.History;
    }

    // 函数功能：按会话ID读取完整事件历史，不做 scope 过滤；会话不存在时返回空列表
    /// <summary>
    /// Reads canonical session events by session identifier without applying a provider-scope filter.
    /// </summary>
    /// <param name="sessionId">Local session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The canonical event list when the session exists; otherwise an empty list.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    public async Task<IReadOnlyList<AgentEvent>> ReadEventsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var projection = await TryProjectSessionAsync(sessionId, includeHistory: true, cancellationToken).ConfigureAwait(false);
        return projection?.History ?? [];
    }

    // 函数功能：将会话状态快照作为 SessionState 事件追加写入 JSONL 文件
    /// <inheritdoc />
    public async Task UpsertStateAsync(
        AgentSessionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sessionFile = await GetExistingSessionFilePathAsync(state.SessionId, cancellationToken).ConfigureAwait(false);
        var ProviderId = await ResolveProviderIdAsync(state.SessionId, state.ProviderKey, cancellationToken).ConfigureAwait(false);
        var snapshotEvent = new AgentRawEvent(
            ProviderId,
            state.SessionId,
            state.UpdatedAt,
            SessionStateEventType,
            JsonSerializer.SerializeToElement(state, AgentJsonSerializerContext.Default.AgentSessionState),
            null);

        await AppendLinesAsync(sessionFile, [snapshotEvent.ToJson()], cancellationToken).ConfigureAwait(false);
    }

    // 函数功能：按 scope 获取会话状态，scope 不匹配或状态不存在时返回 null
    /// <inheritdoc />
    public async Task<AgentSessionState?> GetStateAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var projection = await TryProjectSessionAsync(sessionId, includeHistory: false, cancellationToken).ConfigureAwait(false);
        if (projection is null || projection.Summary is null || projection.State is null)
        {
            return null;
        }

        return MatchesScope(projection.Summary, protocolFamily, providerKey)
            ? projection.State
            : null;
    }

    // 函数功能：按会话ID获取最新会话状态（无 scope 过滤）
    /// <inheritdoc />
    public async Task<AgentSessionState?> GetStateAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var projection = await TryProjectSessionAsync(sessionId, includeHistory: false, cancellationToken).ConfigureAwait(false);
        return projection?.State;
    }

    // 函数功能：按 scope 删除会话文件，清理缓存及空目录；scope 不匹配或文件不存在则返回 false
    /// <inheritdoc />
    public async Task<bool> DeleteSessionAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionFile = await TryGetSessionFilePathAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionFile is null || !File.Exists(sessionFile))
        {
            return false;
        }

        var projection = await TryProjectSessionAsync(sessionId, includeHistory: false, cancellationToken).ConfigureAwait(false);
        if (projection?.Summary is not null && !MatchesScope(projection.Summary, protocolFamily, providerKey))
        {
            return false;
        }

        var deleted = false;
        await _journalFile.WithPathLockAsync(
                sessionFile,
                () =>
                {
                    if (!File.Exists(sessionFile))
                    {
                        return Task.CompletedTask;
                    }

                    File.Delete(sessionFile);
                    _sessionFiles.TryRemove(sessionId, out _);
                    InvalidateMetadataProjectionCache(sessionFile);
                    DeleteEmptySessionDirectories(Path.GetDirectoryName(sessionFile));
                    deleted = true;
                    return Task.CompletedTask;
                },
                cancellationToken)
            .ConfigureAwait(false);
        return deleted;
    }

    // 函数功能：按会话ID删除会话文件（无 scope 过滤），清理缓存及空目录；不存在则返回 false
    /// <inheritdoc />
    public async Task<bool> DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var sessionFile = await TryGetSessionFilePathAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionFile is null || !File.Exists(sessionFile))
        {
            return false;
        }

        var deleted = false;
        await _journalFile.WithPathLockAsync(
                sessionFile,
                () =>
                {
                    if (!File.Exists(sessionFile))
                    {
                        return Task.CompletedTask;
                    }

                    File.Delete(sessionFile);
                    _sessionFiles.TryRemove(sessionId, out _);
                    InvalidateMetadataProjectionCache(sessionFile);
                    DeleteEmptySessionDirectories(Path.GetDirectoryName(sessionFile));
                    deleted = true;
                    return Task.CompletedTask;
                },
                cancellationToken)
            .ConfigureAwait(false);
        return deleted;
    }

    // 函数功能：获取会话文件路径，不存在则按创建时间生成新路径并写入缓存
    private async Task<string> GetOrCreateSessionFilePathAsync(
        string sessionId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var existing = await TryGetSessionFilePathAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var sessionFile = _layout.GetSessionFilePath(sessionId, createdAt);
        _sessionFiles[sessionId] = sessionFile;
        return sessionFile;
    }

    // 函数功能：获取已存在的会话文件路径，找不到则抛出 InvalidOperationException
    private async Task<string> GetExistingSessionFilePathAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var sessionFile = await TryGetSessionFilePathAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionFile is null)
        {
            throw new InvalidOperationException($"Local session '{sessionId}' does not exist.");
        }

        return sessionFile;
    }

    // 函数功能：尝试从缓存或磁盘查找会话文件路径，未找到时返回 null
    private async Task<string?> TryGetSessionFilePathAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessionFiles.TryGetValue(sessionId, out var cachedPath) && File.Exists(cachedPath))
        {
            return cachedPath;
        }

        if (!Directory.Exists(_layout.SessionsRootPath))
        {
            return null;
        }

        foreach (var sessionFile in Directory.EnumerateFiles(_layout.SessionsRootPath, "*.jsonl", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(
                    Path.GetFileNameWithoutExtension(sessionFile),
                    sessionId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            _sessionFiles[sessionId] = sessionFile;
            return sessionFile;
        }

        return null;
    }

    // 函数功能：解析会话的 ProviderId，优先从已有摘要获取，否则用 providerKey 构造默认值
    private async Task<ModelProviderId> ResolveProviderIdAsync(
        string sessionId,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var projection = await TryProjectSessionAsync(sessionId, includeHistory: false, cancellationToken).ConfigureAwait(false);
        return projection?.Summary?.ProviderId ?? new ModelProviderId(providerKey);
    }

    // 函数功能：根据会话ID定位文件并投影，文件不存在时返回 null；includeHistory 控制是否读取事件历史
    private async Task<SessionProjection?> TryProjectSessionAsync(
        string sessionId,
        bool includeHistory,
        CancellationToken cancellationToken)
    {
        var sessionFile = await TryGetSessionFilePathAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionFile is null || !File.Exists(sessionFile))
        {
            return null;
        }

        return await ProjectSessionFileAsync(sessionFile, includeHistory, cancellationToken).ConfigureAwait(false);
    }

    // 函数功能：以文件路径锁保护地对单个 JSONL 文件执行投影，按 includeHistory 选择完整或仅元数据投影
    private async Task<SessionProjection> ProjectSessionFileAsync(
        string sessionFile,
        bool includeHistory,
        CancellationToken cancellationToken)
    {
        return await _journalFile.WithPathLockAsync(
                sessionFile,
                () => includeHistory
                    ? ProjectSessionFileWithHistoryAsync(sessionFile, cancellationToken)
                    : ProjectSessionMetadataFileAsync(sessionFile, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：带缓存的元数据投影；文件未变（stamp 相同）时直接返回缓存结果，否则重新读取并更新缓存
    private async Task<SessionProjection> ProjectSessionMetadataFileAsync(
        string sessionFile,
        CancellationToken cancellationToken)
    {
        var cacheKey = Path.GetFullPath(sessionFile);
        var before = GetFileStamp(sessionFile);
        if (before is not null &&
            _metadataProjectionCache.TryGetValue(cacheKey, out var cached) &&
            cached.Stamp == before)
        {
            return cached.Projection;
        }

        var projection = await ProjectSessionMetadataFileUncachedAsync(sessionFile, cancellationToken).ConfigureAwait(false);
        var after = GetFileStamp(sessionFile);
        if (before is not null && before == after)
        {
            _metadataProjectionCache[cacheKey] = new CachedSessionProjection(before.Value, projection);
        }

        return projection;
    }

    // 函数功能：无缓存地读取文件头尾探针行，从 JSON 行中提取摘要与状态并规范化
    private async Task<SessionProjection> ProjectSessionMetadataFileUncachedAsync(
        string sessionFile,
        CancellationToken cancellationToken)
    {
        AgentSessionSummary? summary = null;
        AgentSessionState? state = null;

        foreach (var line in await ReadMetadataProbeLinesAsync(sessionFile, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                ProjectMetadataSnapshot(document.RootElement, ref summary, ref state);
            }
            catch (JsonException)
            {
            }
        }

        return NormalizeProjection(new SessionProjection(summary, state, []));
    }

    // 函数功能：读取文件头部和（大文件时的）尾部探针行，供元数据投影使用；避免读取整个大文件
    private static async Task<IReadOnlyList<string>> ReadMetadataProbeLinesAsync(
        string sessionFile,
        CancellationToken cancellationToken)
    {
        var length = new FileInfo(sessionFile).Length;
        if (length <= 0)
        {
            return [];
        }

        if (length <= MetadataProbeTailByteCount)
        {
            return await ReadHeadLinesAsync(sessionFile, (int)length, cancellationToken).ConfigureAwait(false);
        }

        var lines = new List<string>(await ReadHeadLinesAsync(sessionFile, MetadataProbeHeadByteCount, cancellationToken).ConfigureAwait(false));
        lines.AddRange(await ReadTailLinesAsync(sessionFile, MetadataProbeTailByteCount, cancellationToken).ConfigureAwait(false));
        return lines;
    }

    // 函数功能：完整读取 JSONL 文件，从中提取摘要、状态及所有用户可见事件历史
    private async Task<SessionProjection> ProjectSessionFileWithHistoryAsync(
        string sessionFile,
        CancellationToken cancellationToken)
    {
        AgentSessionSummary? summary = null;
        AgentSessionState? state = null;
        var history = new List<AgentEvent>();

        await foreach (var @event in ReadJournalEventsAsync(sessionFile, cancellationToken).ConfigureAwait(false))
        {
            if (@event is AgentRawEvent rawEvent)
            {
                if (rawEvent.BackendEventType == SessionSummaryEventType)
                {
                    var snapshot = rawEvent.Raw.Deserialize(AgentJsonSerializerContext.Default.AgentSessionSummary);
                    if (snapshot is not null)
                    {
                        summary = MergeSummarySnapshot(summary, snapshot);
                    }

                    continue;
                }

                if (rawEvent.BackendEventType == SessionStateEventType)
                {
                    var snapshot = rawEvent.Raw.Deserialize(AgentJsonSerializerContext.Default.AgentSessionState);
                    if (snapshot is not null)
                    {
                        state = snapshot;
                    }

                    continue;
                }

                if (rawEvent.BackendEventType is CodeAltaSessionHeaderEventType or CodeAltaSessionStateEventType)
                {
                    continue;
                }
            }

            history.Add(@event);
        }

        return NormalizeProjection(new SessionProjection(summary, state, history));
    }

    // 函数功能：从单条 JSON 元素中解析摘要或状态快照并就地更新 summary/state（ref 参数）
    private static void ProjectMetadataSnapshot(
        JsonElement element,
        ref AgentSessionSummary? summary,
        ref AgentSessionState? state)
    {
        if (!element.TryGetProperty("$type", out var typeElement) ||
            !string.Equals(typeElement.GetString(), "raw", StringComparison.Ordinal))
        {
            return;
        }

        if (!element.TryGetProperty("backendEventType", out var eventTypeElement) ||
            !element.TryGetProperty("raw", out var rawElement))
        {
            return;
        }

        var eventType = eventTypeElement.GetString();
        if (string.Equals(eventType, SessionSummaryEventType, StringComparison.Ordinal))
        {
            var snapshot = rawElement.Deserialize(AgentJsonSerializerContext.Default.AgentSessionSummary);
            if (snapshot is not null)
            {
                summary = MergeSummarySnapshot(summary, snapshot);
            }

            return;
        }

        if (string.Equals(eventType, SessionStateEventType, StringComparison.Ordinal))
        {
            var snapshot = rawElement.Deserialize(AgentJsonSerializerContext.Default.AgentSessionState);
            if (snapshot is not null)
            {
                state = snapshot;
            }
        }
    }

    // 函数功能：对投影结果中的摘要和状态进行规范化（填充空白字段等），返回新投影记录
    private static SessionProjection NormalizeProjection(SessionProjection projection)
    {
        var summary = NormalizeSummary(projection.Summary);
        var state = NormalizeState(projection.State, summary);
        return projection with { Summary = summary, State = state };
    }

    // 函数功能：规范化会话摘要，确保 ProviderId/ProviderKey/ProtocolFamily 不为 null 或空白
    private static AgentSessionSummary? NormalizeSummary(AgentSessionSummary? summary)
    {
        if (summary is null)
        {
            return null;
        }

        var providerKey = NormalizeOptionalText(summary.ProviderKey)
            ?? NormalizeOptionalText(summary.ProviderId.Value)
            ?? string.Empty;
        var ProviderId = string.IsNullOrWhiteSpace(summary.ProviderId.Value)
            ? new ModelProviderId(providerKey)
            : summary.ProviderId;
        return summary with
        {
            ProviderId = ProviderId,
            ProviderKey = providerKey,
            ProtocolFamily = summary.ProtocolFamily ?? string.Empty,
        };
    }

    // 函数功能：规范化会话状态，用摘要值回填缺失的 ProviderKey 和 ProtocolFamily
    private static AgentSessionState? NormalizeState(AgentSessionState? state, AgentSessionSummary? summary)
    {
        if (state is null)
        {
            return null;
        }

        return state with
        {
            ProviderKey = NormalizeOptionalText(state.ProviderKey) ?? summary?.ProviderKey ?? string.Empty,
            ProtocolFamily = NormalizeOptionalText(state.ProtocolFamily) ?? summary?.ProtocolFamily ?? string.Empty,
        };
    }

    // 函数功能：对可选字符串去空白并返回，若为纯空白则返回 null
    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // 函数功能：合并两个摘要快照，优先保留新快照字段，但以旧快照补齐 Parent/CreatedBy 等历史关联字段
    private static AgentSessionSummary MergeSummarySnapshot(
        AgentSessionSummary? current,
        AgentSessionSummary snapshot)
    {
        if (current is null)
        {
            return snapshot;
        }

        return snapshot with
        {
            ParentSessionId = NormalizeOptionalText(snapshot.ParentSessionId) ?? NormalizeOptionalText(current.ParentSessionId),
            CreatedBySessionId = NormalizeOptionalText(snapshot.CreatedBySessionId) ?? NormalizeOptionalText(current.CreatedBySessionId),
            CreatedByRunId = snapshot.CreatedByRunId ?? current.CreatedByRunId,
        };
    }

    // 函数功能：逐行读取 JSONL 文件并反序列化为 AgentEvent，末行 JSON 损坏时提前结束枚举
    private async IAsyncEnumerable<AgentEvent> ReadJournalEventsAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await OpenReadStreamAsync(path, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Utf8WithoutBom, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AgentEvent? @event;
            try
            {
                @event = JsonSerializer.Deserialize(line, AgentJsonSerializerContext.Default.AgentEvent)
                    ?? throw new JsonException("Journal line deserialized to null.");
            }
            catch (JsonException) when (reader.Peek() < 0)
            {
                yield break;
            }

            yield return @event;
        }
    }

    // 函数功能：从文件尾部读取最多 byteCount 字节并按行拆分，用于大文件的尾部探针
    private static async Task<IReadOnlyList<string>> ReadTailLinesAsync(
        string path,
        int byteCount,
        CancellationToken cancellationToken)
    {
        await using var stream = await OpenReadStreamAsync(path, cancellationToken).ConfigureAwait(false);
        var length = stream.Length;
        var count = (int)Math.Min(byteCount, length);
        if (count == 0)
        {
            return [];
        }

        var buffer = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            stream.Seek(-count, SeekOrigin.End);
            var read = 0;
            while (read < count)
            {
                var current = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
                if (current == 0)
                {
                    break;
                }

                read += current;
            }

            var text = Utf8WithoutBom.GetString(buffer, 0, read);
            if (count < length)
            {
                var firstNewline = text.IndexOf('\n');
                text = firstNewline >= 0 ? text[(firstNewline + 1)..] : string.Empty;
            }

            return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // 函数功能：从文件头部读取最多 byteCount 字节并按行拆分，用于元数据探针
    private static async Task<IReadOnlyList<string>> ReadHeadLinesAsync(
        string path,
        int byteCount,
        CancellationToken cancellationToken)
    {
        await using var stream = await OpenReadStreamAsync(path, cancellationToken).ConfigureAwait(false);
        var length = stream.Length;
        var count = (int)Math.Min(byteCount, length);
        if (count == 0)
        {
            return [];
        }

        var buffer = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            var read = 0;
            while (read < count)
            {
                var current = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
                if (current == 0)
                {
                    break;
                }

                read += current;
            }

            var text = Utf8WithoutBom.GetString(buffer, 0, read);
            if (count < length)
            {
                var lastNewline = text.LastIndexOf('\n');
                text = lastNewline >= 0 ? text[..lastNewline] : string.Empty;
            }

            return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // 函数功能：确保目录存在后，将多行文本追加到 JSONL 文件，并使缓存失效
    private async Task AppendLinesAsync(
        string path,
        IReadOnlyList<string> lines,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Path '{path}' did not resolve to a parent directory.");
        Directory.CreateDirectory(directory);

        await _journalFile.AppendLinesAsync(path, lines, Utf8WithoutBom, cancellationToken)
            .ConfigureAwait(false);
        InvalidateMetadataProjectionCache(path);
    }

    // 函数功能：以读共享模式异步打开文件流，失败时自动重试（ReadRetryTime 间隔）
    private static Task<FileStream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
        => AgentSessionJournalFile.RetryFileOperationAsync(
            () => Task.FromResult(new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true)),
            ReadRetryTime,
            cancellationToken);

    // 函数功能：获取文件的修改时间与大小作为缓存校验戳，文件不存在时返回 null
    private static FileStamp? GetFileStamp(string path)
    {
        var fileInfo = new FileInfo(path);
        return fileInfo.Exists
            ? new FileStamp(fileInfo.LastWriteTimeUtc, fileInfo.Length)
            : null;
    }

    // 函数功能：从元数据投影缓存中移除指定文件路径的缓存条目
    private void InvalidateMetadataProjectionCache(string path)
        => _metadataProjectionCache.TryRemove(Path.GetFullPath(path), out _);

    // 函数功能：检查会话摘要的协议族与提供方键是否与给定 scope 匹配（不区分大小写）
    private static bool MatchesScope(AgentSessionSummary summary, string protocolFamily, string providerKey)
    {
        return string.Equals(summary.ProtocolFamily, protocolFamily, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(summary.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase);
    }

    // 函数功能：按过滤器条件（Cwd/GitRoot/Repository/Branch）匹配会话元数据，filter 为 null 时全部匹配
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

    // 函数功能：将摘要和状态转换为对外公开的 AgentSessionMetadata 对象
    private static AgentSessionMetadata ToMetadata(
        AgentSessionSummary summary,
        AgentSessionState? state)
        => new(
            summary.SessionId,
            summary.CreatedAt,
            summary.UpdatedAt,
            summary.Summary,
            summary.WorkingDirectory is null ? null : new AgentSessionContext(summary.WorkingDirectory),
            summary.WorkingDirectory,
            new RawApiSessionMetadataDetails(
                ProviderSessionId: state?.ProviderSessionId,
                Title: summary.Title),
            summary.ProtocolFamily,
            summary.ProviderKey,
            summary.ModelId,
            summary.ParentSessionId,
            summary.CreatedBySessionId,
            summary.CreatedByRunId);

    // 函数功能：删除会话文件后，向上递归清理 sessions 根目录以内的所有空目录
    private void DeleteEmptySessionDirectories(string? directory)
    {
        var sessionsRoot = Path.GetFullPath(_layout.SessionsRootPath);
        while (!string.IsNullOrWhiteSpace(directory) &&
               Directory.Exists(directory) &&
               Path.GetFullPath(directory).StartsWith(sessionsRoot, StringComparison.OrdinalIgnoreCase) &&
               !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            if (string.Equals(Path.GetFullPath(directory), sessionsRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            directory = Path.GetDirectoryName(directory);
        }
    }

    // 类型：会话投影结果，包含摘要、最新状态及完整事件历史
    private sealed record SessionProjection(
        AgentSessionSummary? Summary,
        AgentSessionState? State,
        IReadOnlyList<AgentEvent> History);

    // 类型：列举时附带文件路径的会话投影包装
    private sealed record ListedSessionProjection(string SessionFile, SessionProjection Projection);

    // 类型：文件校验戳，用于判断元数据投影缓存是否仍然有效
    private readonly record struct FileStamp(DateTime LastWriteTimeUtc, long Length);

    // 类型：带文件戳的缓存投影条目
    private sealed record CachedSessionProjection(FileStamp Stamp, SessionProjection Projection);
}
