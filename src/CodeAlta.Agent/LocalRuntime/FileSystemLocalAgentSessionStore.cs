using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CodeAlta.Agent.LocalRuntime;

/// <summary>
/// Persists local agent providers, sessions, state, and canonical event logs on the filesystem.
/// </summary>
public sealed class FileSystemLocalAgentSessionStore : ILocalAgentSessionStore
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly LocalAgentRuntimePathLayout _layout;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _sessionRoots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemLocalAgentSessionStore"/> class.
    /// </summary>
    /// <param name="layout">Filesystem layout.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="layout"/> is <see langword="null" />.</exception>
    public FileSystemLocalAgentSessionStore(LocalAgentRuntimePathLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <inheritdoc />
    public async Task<LocalAgentProviderDescriptor> UpsertProviderAsync(
        LocalAgentProviderDescriptor provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var providerRoot = _layout.GetProviderRootPath(provider.ProtocolFamily, provider.ProviderKey);
        var providerPath = _layout.GetProviderDescriptorPath(provider.ProtocolFamily, provider.ProviderKey);
        Directory.CreateDirectory(providerRoot);

        await WriteFileAtomicallyAsync(
            providerPath,
            provider.ToJson(),
            cancellationToken).ConfigureAwait(false);

        return provider;
    }

    /// <inheritdoc />
    public async Task<LocalAgentProviderDescriptor?> GetProviderAsync(
        string protocolFamily,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var providerPath = _layout.GetProviderDescriptorPath(protocolFamily, providerKey);
        if (!File.Exists(providerPath))
        {
            return null;
        }

        return await ReadJsonFileAsync(
            providerPath,
            AgentJsonSerializerContext.Default.LocalAgentProviderDescriptor,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertSessionAsync(
        LocalAgentSessionSummary session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var sessionRoot = await GetOrCreateSessionRootPathAsync(
            session.ProtocolFamily,
            session.ProviderKey,
            session.SessionId,
            session.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(sessionRoot);
        Directory.CreateDirectory(_layout.GetAttachmentsDirectoryPath(sessionRoot));

        await WriteFileAtomicallyAsync(
            _layout.GetSessionSummaryPath(sessionRoot),
            session.ToJson(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LocalAgentSessionSummary?> GetSessionAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionRoot = await TryGetSessionRootPathAsync(
            protocolFamily,
            providerKey,
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (sessionRoot is null)
        {
            return null;
        }

        var summaryPath = _layout.GetSessionSummaryPath(sessionRoot);
        if (!File.Exists(summaryPath))
        {
            return null;
        }

        return await ReadJsonFileAsync(
            summaryPath,
            AgentJsonSerializerContext.Default.LocalAgentSessionSummary,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalAgentSessionSummary>> ListSessionsAsync(
        string protocolFamily,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var sessionsRoot = _layout.GetProviderSessionsRootPath(protocolFamily, providerKey);
        if (!Directory.Exists(sessionsRoot))
        {
            return [];
        }

        var results = new List<LocalAgentSessionSummary>();
        foreach (var sessionFile in Directory.EnumerateFiles(sessionsRoot, "session.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var summary = await ReadJsonFileAsync(
                    sessionFile,
                    AgentJsonSerializerContext.Default.LocalAgentSessionSummary,
                    cancellationToken).ConfigureAwait(false);
                if (summary is null)
                {
                    continue;
                }

                if (!string.Equals(summary.ProtocolFamily, protocolFamily, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(summary.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(summary);
                _sessionRoots[GetSessionCacheKey(protocolFamily, providerKey, summary.SessionId)] =
                    Path.GetDirectoryName(sessionFile)
                    ?? throw new InvalidOperationException($"Session path '{sessionFile}' did not resolve to a directory.");
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
        }

        return results
            .OrderByDescending(static session => session.UpdatedAt)
            .ThenByDescending(static session => session.CreatedAt)
            .ToArray();
    }

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

        var sessionRoot = await GetExistingSessionRootPathAsync(
            protocolFamily,
            providerKey,
            sessionId,
            cancellationToken).ConfigureAwait(false);
        var eventsPath = _layout.GetSessionEventsPath(sessionRoot);
        Directory.CreateDirectory(sessionRoot);

        var pathLock = GetPathLock(eventsPath);
        await pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(
                eventsPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, Utf8WithoutBom);
            foreach (var @event in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(@event.ToJson().AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            pathLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentEvent>> ReadEventsAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionRoot = await TryGetSessionRootPathAsync(
            protocolFamily,
            providerKey,
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (sessionRoot is null)
        {
            return [];
        }

        var eventsPath = _layout.GetSessionEventsPath(sessionRoot);
        if (!File.Exists(eventsPath))
        {
            return [];
        }

        var results = new List<AgentEvent>();
        await using var stream = new FileStream(
            eventsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream, Utf8WithoutBom, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var @event = JsonSerializer.Deserialize(line, AgentJsonSerializerContext.Default.AgentEvent)
                    ?? throw new JsonException("Event log line deserialized to null.");
                results.Add(@event);
            }
            catch (JsonException) when (reader.Peek() < 0)
            {
                break;
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task UpsertStateAsync(
        LocalAgentSessionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sessionRoot = await GetExistingSessionRootPathAsync(
            state.ProtocolFamily,
            state.ProviderKey,
            state.SessionId,
            cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(sessionRoot);

        await WriteFileAtomicallyAsync(
            _layout.GetSessionStatePath(sessionRoot),
            state.ToJson(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LocalAgentSessionState?> GetStateAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionRoot = await TryGetSessionRootPathAsync(
            protocolFamily,
            providerKey,
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (sessionRoot is null)
        {
            return null;
        }

        var statePath = _layout.GetSessionStatePath(sessionRoot);
        if (!File.Exists(statePath))
        {
            return null;
        }

        return await ReadJsonFileAsync(
            statePath,
            AgentJsonSerializerContext.Default.LocalAgentSessionState,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSessionAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionRoot = await TryGetSessionRootPathAsync(
            protocolFamily,
            providerKey,
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (sessionRoot is null || !Directory.Exists(sessionRoot))
        {
            return false;
        }

        var pathLock = GetPathLock(sessionRoot);
        await pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(sessionRoot))
            {
                return false;
            }

            Directory.Delete(sessionRoot, recursive: true);
            _sessionRoots.TryRemove(GetSessionCacheKey(protocolFamily, providerKey, sessionId), out _);
            return true;
        }
        finally
        {
            pathLock.Release();
        }
    }

    private async Task<string> GetOrCreateSessionRootPathAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var existing = await TryGetSessionRootPathAsync(protocolFamily, providerKey, sessionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var sessionRoot = _layout.GetSessionRootPath(protocolFamily, providerKey, sessionId, createdAt);
        _sessionRoots[GetSessionCacheKey(protocolFamily, providerKey, sessionId)] = sessionRoot;
        return sessionRoot;
    }

    private async Task<string> GetExistingSessionRootPathAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var sessionRoot = await TryGetSessionRootPathAsync(protocolFamily, providerKey, sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionRoot is null)
        {
            throw new InvalidOperationException(
                $"Local session '{protocolFamily}/{providerKey}/{sessionId}' does not exist.");
        }

        return sessionRoot;
    }

    private async Task<string?> TryGetSessionRootPathAsync(
        string protocolFamily,
        string providerKey,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var cacheKey = GetSessionCacheKey(protocolFamily, providerKey, sessionId);
        if (_sessionRoots.TryGetValue(cacheKey, out var cachedRoot) && Directory.Exists(cachedRoot))
        {
            return cachedRoot;
        }

        var sessionsRoot = _layout.GetProviderSessionsRootPath(protocolFamily, providerKey);
        if (!Directory.Exists(sessionsRoot))
        {
            return null;
        }

        foreach (var sessionFile in Directory.EnumerateFiles(sessionsRoot, "session.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var summary = await ReadJsonFileAsync(
                    sessionFile,
                    AgentJsonSerializerContext.Default.LocalAgentSessionSummary,
                    cancellationToken).ConfigureAwait(false);
                if (summary is null || !string.Equals(summary.SessionId, sessionId, StringComparison.Ordinal))
                {
                    continue;
                }

                var sessionRoot = Path.GetDirectoryName(sessionFile)
                    ?? throw new InvalidOperationException($"Session path '{sessionFile}' did not resolve to a directory.");
                _sessionRoots[cacheKey] = sessionRoot;
                return sessionRoot;
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private async Task WriteFileAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Path '{path}' did not resolve to a parent directory.");
        Directory.CreateDirectory(directory);

        var pathLock = GetPathLock(path);
        await pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(tempPath, content, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Move(tempPath, path, overwrite: true);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        finally
        {
            pathLock.Release();
        }
    }

    private SemaphoreSlim GetPathLock(string path)
        => _pathLocks.GetOrAdd(path, static _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));

    private static async Task<T?> ReadJsonFileAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
    }

    private static string GetSessionCacheKey(string protocolFamily, string providerKey, string sessionId)
        => $"{protocolFamily}\n{providerKey}\n{sessionId}";
}
