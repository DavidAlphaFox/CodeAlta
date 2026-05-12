using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeAlta.Agent;
using CodeAlta.Agent.LocalRuntime;

namespace CodeAlta.Catalog;

/// <summary>
/// Stores durable thread metadata in the session JSONL journal.
/// </summary>
public sealed class WorkThreadJournalStore
{
    /// <summary>
    /// Backend raw-event type used for the first-line thread header.
    /// </summary>
    public const string ThreadHeaderEventType = "codealta.threadHeader";

    /// <summary>
    /// Backend raw-event type used for append-only thread state snapshots.
    /// </summary>
    public const string ThreadStateEventType = "codealta.threadState";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly LocalAgentRuntimePathLayout _layout;
    private readonly LocalAgentSessionJournalFile _journalFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkThreadJournalStore" /> class.
    /// </summary>
    /// <param name="options">Catalog options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    public WorkThreadJournalStore(CatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _layout = new LocalAgentRuntimePathLayout(options.GlobalRoot);
        _journalFile = new LocalAgentSessionJournalFile();
    }

    /// <summary>
    /// Ensures a missing or empty session journal starts with a CodeAlta thread header.
    /// </summary>
    /// <param name="thread">Thread descriptor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task EnsureHeaderAsync(WorkThreadDescriptor thread, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (string.IsNullOrWhiteSpace(thread.ThreadId) || thread.CreatedAt == default)
        {
            return;
        }

        await _journalFile.EnsureFirstLineAsync(
                GetPath(thread.ThreadId, thread.CreatedAt),
                CreateHeaderEvent(thread).ToJson(),
                Utf8WithoutBom,
                IsThreadHeaderLine,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Appends a thread state snapshot to the session journal.
    /// </summary>
    /// <param name="thread">Thread descriptor.</param>
    /// <param name="state">State snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AppendStateAsync(WorkThreadDescriptor thread, WorkThreadLocalState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(thread.ThreadId) || thread.CreatedAt == default)
        {
            return;
        }

        await _journalFile.AppendLinesWithRequiredFirstLineAsync(
                GetPath(thread.ThreadId, thread.CreatedAt),
                CreateHeaderEvent(thread).ToJson(),
                [CreateStateEvent(thread, state).ToJson()],
                Utf8WithoutBom,
                IsThreadHeaderLine,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the first-line thread header for a journal.
    /// </summary>
    /// <param name="threadId">Thread identifier.</param>
    /// <param name="createdAt">Thread creation timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The header, or <see langword="null" /> when the journal has no header.</returns>
    public async Task<WorkThreadJournalHeader?> ReadHeaderAsync(string threadId, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        if (createdAt == default)
        {
            return null;
        }

        return await ReadHeaderFromPathAsync(GetPath(threadId, createdAt), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists first-line thread headers from all session journals.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The discovered headers.</returns>
    public async Task<IReadOnlyList<WorkThreadJournalHeader>> ListHeadersAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_layout.SessionsRootPath))
        {
            return [];
        }

        var results = new List<WorkThreadJournalHeader>();
        foreach (var path in Directory.EnumerateFiles(_layout.SessionsRootPath, "*.jsonl", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var header = await ReadHeaderFromPathAsync(path, cancellationToken).ConfigureAwait(false);
            if (header is not null)
            {
                results.Add(header);
            }
        }

        return results;
    }

    /// <summary>
    /// Reads the latest state snapshot by probing near the end of the journal.
    /// </summary>
    /// <param name="threadId">Thread identifier.</param>
    /// <param name="createdAt">Thread creation timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest state snapshot, or <see langword="null" />.</returns>
    public async Task<WorkThreadLocalState?> ReadLatestStateAsync(string threadId, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        if (createdAt == default)
        {
            return null;
        }

        var path = GetPath(threadId, createdAt);
        if (!File.Exists(path))
        {
            return null;
        }

        await foreach (var line in ReadLinesFromEndAsync(path, 64 * 1024, cancellationToken).ConfigureAwait(false))
        {
            if (!TryDeserializeRawEvent(line, out var rawEvent) || rawEvent.BackendEventType != ThreadStateEventType)
            {
                continue;
            }

            return rawEvent.Raw.Deserialize(WorkThreadJournalJsonSerializerContext.Default.WorkThreadLocalState);
        }

        return null;
    }

    private string GetPath(string threadId, DateTimeOffset createdAt)
        => _layout.GetSessionFilePath(threadId, createdAt);

    private static AgentRawEvent CreateHeaderEvent(WorkThreadDescriptor thread)
        => new(
            new AgentBackendId(thread.BackendId),
            thread.ThreadId,
            thread.CreatedAt,
            ThreadHeaderEventType,
            JsonSerializer.SerializeToElement(WorkThreadJournalHeader.FromDescriptor(thread), WorkThreadJournalJsonSerializerContext.Default.WorkThreadJournalHeader));

    private static AgentRawEvent CreateStateEvent(WorkThreadDescriptor thread, WorkThreadLocalState state)
        => new(
            new AgentBackendId(thread.BackendId),
            thread.ThreadId,
            DateTimeOffset.UtcNow,
            ThreadStateEventType,
            JsonSerializer.SerializeToElement(state, WorkThreadJournalJsonSerializerContext.Default.WorkThreadLocalState));

    private async Task<WorkThreadJournalHeader?> ReadHeaderFromPathAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        using var reader = new StreamReader(stream, Utf8WithoutBom, detectEncodingFromByteOrderMarks: true);
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line) || !TryDeserializeRawEvent(line, out var rawEvent) || rawEvent.BackendEventType != ThreadHeaderEventType)
        {
            return null;
        }

        return rawEvent.Raw.Deserialize(WorkThreadJournalJsonSerializerContext.Default.WorkThreadJournalHeader);
    }

    private static async IAsyncEnumerable<string> ReadLinesFromEndAsync(
        string path,
        int chunkSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        var length = stream.Length;
        var position = length;
        var carry = string.Empty;
        var skipTrailingEmptyLine = true;
        while (position > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(chunkSize, position);
            position -= count;
            var buffer = ArrayPool<byte>.Shared.Rent(count);
            var read = 0;
            try
            {
                stream.Seek(position, SeekOrigin.Begin);
                while (read < count)
                {
                    var current = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
                    if (current == 0)
                    {
                        break;
                    }

                    read += current;
                }

                var text = Utf8WithoutBom.GetString(buffer, 0, read) + carry;
                var lines = text.Split('\n');
                var firstLineIsPartial = position > 0;
                carry = firstLineIsPartial ? lines[0] : string.Empty;
                var startIndex = firstLineIsPartial ? 1 : 0;
                for (var index = lines.Length - 1; index >= startIndex; index--)
                {
                    var line = lines[index].TrimEnd('\r');
                    if (skipTrailingEmptyLine && line.Length == 0)
                    {
                        skipTrailingEmptyLine = false;
                        continue;
                    }

                    skipTrailingEmptyLine = false;
                    if (line.Length > 0)
                    {
                        yield return line;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        if (carry.Length > 0)
        {
            yield return carry.TrimEnd('\r');
        }
    }

    private static bool TryDeserializeRawEvent(string? line, out RawJournalEvent rawEvent)
    {
        rawEvent = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("$type", out var typeElement) ||
                !string.Equals(typeElement.GetString(), "raw", StringComparison.Ordinal) ||
                !root.TryGetProperty("backendEventType", out var eventTypeElement) ||
                !root.TryGetProperty("raw", out var rawElement))
            {
                return false;
            }

            rawEvent = new RawJournalEvent(eventTypeElement.GetString() ?? string.Empty, rawElement.Clone());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsThreadHeaderLine(string? line)
        => TryDeserializeRawEvent(line, out var rawEvent) && rawEvent.BackendEventType == ThreadHeaderEventType;

    private readonly record struct RawJournalEvent(string BackendEventType, JsonElement Raw);
}

/// <summary>
/// Describes durable first-line thread metadata stored in a session journal.
/// </summary>
public sealed class WorkThreadJournalHeader
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("thread_id")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public WorkThreadKind Kind { get; set; }

    [JsonPropertyName("backend_id")]
    public string BackendId { get; set; } = string.Empty;

    [JsonPropertyName("provider_key")]
    public string? ProviderKey { get; set; }

    [JsonPropertyName("project_ref")]
    public string? ProjectRef { get; set; }

    [JsonPropertyName("parent_thread_id")]
    public string? ParentThreadId { get; set; }

    [JsonPropertyName("created_by")]
    public AltaActorProvenance? CreatedBy { get; set; }

    [JsonPropertyName("working_directory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public static WorkThreadJournalHeader FromDescriptor(WorkThreadDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new WorkThreadJournalHeader
        {
            ThreadId = descriptor.ThreadId,
            Kind = descriptor.Kind,
            BackendId = descriptor.BackendId,
            ProviderKey = descriptor.ProviderKey,
            ProjectRef = descriptor.ProjectRef,
            ParentThreadId = descriptor.ParentThreadId,
            CreatedBy = descriptor.CreatedBy,
            WorkingDirectory = descriptor.WorkingDirectory,
            CreatedAt = descriptor.CreatedAt,
        };
    }

    public WorkThreadDescriptor ToDescriptor()
        => new()
        {
            ThreadId = ThreadId,
            Kind = Kind,
            BackendId = BackendId,
            ProviderKey = ProviderKey,
            ProjectRef = ProjectRef,
            ParentThreadId = ParentThreadId,
            CreatedBy = CreatedBy,
            WorkingDirectory = WorkingDirectory,
            Title = ThreadId,
            Status = WorkThreadStatus.Active,
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt,
            LastActiveAt = CreatedAt,
        };
}

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WorkThreadJournalHeader))]
[JsonSerializable(typeof(WorkThreadLocalState))]
[JsonSerializable(typeof(WorkThreadPreference))]
[JsonSerializable(typeof(AltaActorProvenance))]
[JsonSerializable(typeof(WorkThreadQueuedPrompt))]
[JsonSerializable(typeof(WorkThreadPromptProvenance))]
internal sealed partial class WorkThreadJournalJsonSerializerContext : JsonSerializerContext;
