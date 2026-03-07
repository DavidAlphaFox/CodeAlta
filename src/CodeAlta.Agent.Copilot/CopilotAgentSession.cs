using System.Collections.Concurrent;
using System.Threading.Channels;
using GitHub.Copilot.SDK;
using XenoAtom.Logging;

namespace CodeAlta.Agent.Copilot;

/// <summary>
/// Copilot session-backed implementation of <see cref="IAgentSession"/>.
/// </summary>
public sealed class CopilotAgentSession : ICopilotAgentSession
{
    private static readonly Logger Logger = LogManager.GetLogger("CodeAlta.Agent.Copilot.Session");
    private readonly CopilotAgentBackend _backend;
    private readonly Channel<AgentEvent> _eventChannel;
    private readonly ConcurrentDictionary<Guid, Action<AgentEvent>> _subscribers = new();
    private readonly IDisposable _subscription;
    private bool _disposed;

    internal CopilotAgentSession(CopilotAgentBackend backend, CopilotSession session)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(session);

        _backend = backend;
        Session = session;
        _eventChannel = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _subscription = Session.On(OnSessionEvent);
    }

    /// <inheritdoc />
    public AgentBackendId BackendId => AgentBackendIds.Copilot;

    /// <inheritdoc />
    public string SessionId => Session.SessionId;

    /// <inheritdoc />
    public string? WorkspacePath => Session.WorkspacePath;

    /// <inheritdoc />
    public CopilotSession Session { get; }

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

        var messageOptions = CopilotAgentMapper.ToMessageOptions(options);
        var messageId = await Session.SendAsync(messageOptions, cancellationToken).ConfigureAwait(false);
        return new AgentRunId(messageId);
    }

    /// <inheritdoc />
    public Task AbortAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Session.AbortAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var messages = await Session.GetMessagesAsync(cancellationToken).ConfigureAwait(false);
        return CopilotAgentMapper.ToHistoryEvents(SessionId, messages);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _subscription.Dispose();
        CompleteEventStream();
        await Session.DisposeAsync().ConfigureAwait(false);
        _backend.RemoveSession(SessionId, this);
    }

    internal void Publish(AgentEvent eventData)
    {
        if (!_eventChannel.Writer.TryWrite(eventData))
            return;

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

    private void OnSessionEvent(SessionEvent sessionEvent)
    {
        try
        {
            LogDebug($"Raw Copilot session event session={SessionId} type={sessionEvent.Type} payload={SafeToJson(sessionEvent)}");
            var eventData = CopilotAgentMapper.ToAgentEvent(SessionId, sessionEvent);
            LogDebug($"Mapped Copilot agent event session={SessionId} type={eventData.GetType().Name} payload={eventData.ToJson()}");
            Publish(eventData);
        }
        catch (Exception ex)
        {
            LogError($"Failed to map Copilot session event session={SessionId} type={sessionEvent.Type}", ex);
            Publish(
                new AgentErrorEvent(
                    AgentBackendIds.Copilot,
                    SessionId,
                    DateTimeOffset.UtcNow,
                    ex.Message,
                    ex));
        }
    }

    private void CompleteEventStream()
    {
        _eventChannel.Writer.TryComplete();
    }

    private static void LogDebug(string message)
    {
        if (LogManager.IsInitialized && Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.Debug(message);
        }
    }

    private static void LogError(string message, Exception exception)
    {
        if (LogManager.IsInitialized && Logger.IsEnabled(LogLevel.Error))
        {
            Logger.Error(exception, message);
        }
    }

    private static string SafeToJson(SessionEvent sessionEvent)
    {
        try
        {
            return sessionEvent.ToJson();
        }
        catch (Exception ex)
        {
            return $"<failed to serialize raw session event: {ex.Message}>";
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
