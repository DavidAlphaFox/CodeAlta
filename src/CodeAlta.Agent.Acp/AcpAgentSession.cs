using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using CodeAlta.Acp;

namespace CodeAlta.Agent.Acp;

/// <summary>
/// ACP session-backed implementation of <see cref="IAgentSession"/>.
/// </summary>
public sealed class AcpAgentSession : IAgentSession
{
    private readonly AcpAgentBackend _backend;
    private readonly Channel<AgentEvent> _eventChannel;
    private readonly ConcurrentDictionary<Guid, Action<AgentEvent>> _subscribers = new();
    private readonly AcpHistoryJournal _historyJournal;
    private readonly object _syncRoot = new();
    private string? _systemMessage;
    private string? _developerInstructions;
    private string? _model;
    private AgentReasoningEffort? _reasoningEffort;
    private AgentPermissionRequestHandler _permissionHandler;
    private AgentUserInputRequestHandler? _userInputHandler;
    private AgentRunId? _activeRunId;
    private bool _needsInitialPreamble;
    private bool _disposed;

    internal AcpAgentSession(
        AcpAgentBackend backend,
        string sessionId,
        string? workspacePath,
        string? systemMessage,
        string? developerInstructions,
        string? model,
        AgentReasoningEffort? reasoningEffort,
        AgentPermissionRequestHandler permissionHandler,
        AgentUserInputRequestHandler? userInputHandler,
        AcpHistoryJournal historyJournal)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(permissionHandler);
        ArgumentNullException.ThrowIfNull(historyJournal);

        _backend = backend;
        SessionId = sessionId;
        WorkspacePath = workspacePath;
        _systemMessage = systemMessage;
        _developerInstructions = developerInstructions;
        _model = model;
        _reasoningEffort = reasoningEffort;
        _permissionHandler = permissionHandler;
        _userInputHandler = userInputHandler;
        _historyJournal = historyJournal;
        _needsInitialPreamble = !string.IsNullOrWhiteSpace(systemMessage) || !string.IsNullOrWhiteSpace(developerInstructions);
        _eventChannel = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    /// <inheritdoc />
    public AgentBackendId BackendId => _backend.BackendId;

    /// <inheritdoc />
    public string SessionId { get; }

    /// <inheritdoc />
    public string? WorkspacePath { get; }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> StreamEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _eventChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<AgentEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = Guid.CreateVersion7();
        _subscribers.TryAdd(key, handler);
        return new Unsubscriber(() => _subscribers.TryRemove(key, out _));
    }

    /// <inheritdoc />
    public async Task<AgentRunId> SendAsync(AgentSendOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var runId = new AgentRunId(Guid.CreateVersion7().ToString("N"));
        lock (_syncRoot)
        {
            _activeRunId = runId;
        }

        await _backend.ApplySessionPreferencesAsync(this, _model, _reasoningEffort, cancellationToken).ConfigureAwait(false);

        var prompt = AcpAgentMapper.ToPrompt(
            options.Input,
            includePreamble: ConsumeInitialPreamble(),
            _systemMessage,
            _developerInstructions);
        var response = await _backend.Client.SessionPromptAsync(
                new PromptRequest
                {
                    SessionId = SessionId,
                    Prompt = prompt,
                    MessageId = runId.Value
                },
                cancellationToken)
            .ConfigureAwait(false);

        var timestamp = DateTimeOffset.UtcNow;
        var usage = AcpAgentMapper.ToUsage(response, timestamp);
        if (usage is not null)
        {
            await PublishAsync(
                    new AgentSessionUpdateEvent(
                        BackendId,
                        SessionId,
                        timestamp,
                        runId,
                        AgentSessionUpdateKind.UsageUpdated,
                        Message: null,
                        Details: null,
                        Usage: usage),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await PublishAsync(
                new AgentSessionUpdateEvent(
                    BackendId,
                    SessionId,
                    timestamp,
                    runId,
                    AgentSessionUpdateKind.Idle,
                    $"Prompt completed ({AcpJsonHelpers.GetStringValue(response.StopReason.Value) ?? "completed"}).",
                    Details: response.Meta is null ? null : JsonSerializer.SerializeToElement(response.Meta),
                    Usage: usage),
                cancellationToken)
            .ConfigureAwait(false);

        lock (_syncRoot)
        {
            _activeRunId = null;
        }

        return runId;
    }

    /// <inheritdoc />
    public Task<AgentRunId> SteerAsync(AgentSteerOptions options, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Generic ACP backends do not support steer semantics.");

    /// <inheritdoc />
    public Task AbortAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _backend.Client.SessionCancelAsync(
            new CancelNotification
            {
                SessionId = SessionId
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task CompactAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Generic ACP backends do not support manual compaction.");

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _historyJournal.ReadAllAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CompleteEventStream();
        await _backend.ReleaseSessionAsync(this).ConfigureAwait(false);
    }

    internal void UpdateOptions(
        string? systemMessage,
        string? developerInstructions,
        string? model,
        AgentReasoningEffort? reasoningEffort,
        AgentPermissionRequestHandler permissionHandler,
        AgentUserInputRequestHandler? userInputHandler)
    {
        ArgumentNullException.ThrowIfNull(permissionHandler);

        lock (_syncRoot)
        {
            _systemMessage = systemMessage;
            _developerInstructions = developerInstructions;
            _model = model;
            _reasoningEffort = reasoningEffort;
            _permissionHandler = permissionHandler;
            _userInputHandler = userInputHandler;
            if (!string.IsNullOrWhiteSpace(systemMessage) || !string.IsNullOrWhiteSpace(developerInstructions))
            {
                _needsInitialPreamble = true;
            }
        }
    }

    internal AgentRunId? GetActiveRunId()
    {
        lock (_syncRoot)
        {
            return _activeRunId;
        }
    }

    internal AgentPermissionRequestHandler GetPermissionHandler()
    {
        lock (_syncRoot)
        {
            return _permissionHandler;
        }
    }

    internal AgentUserInputRequestHandler? GetUserInputHandler()
    {
        lock (_syncRoot)
        {
            return _userInputHandler;
        }
    }

    internal async Task PublishAsync(AgentEvent eventData, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (_eventChannel.Writer.TryWrite(eventData))
        {
            foreach (var subscriber in _subscribers.Values)
            {
                try
                {
                    subscriber(eventData);
                }
                catch
                {
                }
            }
        }

        await _historyJournal.AppendAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    internal void CompleteEventStream()
    {
        _eventChannel.Writer.TryComplete();
    }

    private bool ConsumeInitialPreamble()
    {
        lock (_syncRoot)
        {
            if (!_needsInitialPreamble)
            {
                return false;
            }

            _needsInitialPreamble = false;
            return true;
        }
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose()
        {
            Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }
}
