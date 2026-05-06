using System.Threading.Channels;

namespace CodeAlta.Orchestration.Runtime;

/// <summary>
/// Provides a bounded runtime event stream with a documented newest-event drop policy under pressure.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public sealed class BoundedRuntimeEventStream<TEvent>
{
    /// <summary>The default event stream capacity.</summary>
    public const int DefaultCapacity = 1024;

    private readonly Channel<TEvent> _channel;
    private long _droppedCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoundedRuntimeEventStream{TEvent}"/> class.
    /// </summary>
    /// <param name="capacity">The bounded channel capacity.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is less than one.</exception>
    public BoundedRuntimeEventStream(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Event stream capacity must be at least one.");
        }

        _channel = Channel.CreateBounded<TEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <summary>Gets the approximate number of events dropped because the stream was full.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>
    /// Attempts to publish an event without blocking runtime progress.
    /// </summary>
    /// <param name="runtimeEvent">The event to publish.</param>
    /// <returns><see langword="true"/> when the event was accepted; otherwise, <see langword="false"/> when the stream is complete.</returns>
    public bool TryPublish(TEvent runtimeEvent)
    {
        if (_channel.Writer.TryWrite(runtimeEvent))
        {
            return true;
        }

        Interlocked.Increment(ref _droppedCount);
        return false;
    }

    /// <summary>
    /// Streams events until the stream is completed or cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async event sequence.</returns>
    public IAsyncEnumerable<TEvent> ReadAllAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Completes the stream.
    /// </summary>
    public void Complete() => _channel.Writer.TryComplete();
}
