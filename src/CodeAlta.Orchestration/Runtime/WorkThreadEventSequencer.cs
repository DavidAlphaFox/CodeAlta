namespace CodeAlta.Orchestration.Runtime;

/// <summary>
/// Assigns monotonically increasing sequence numbers to events for each work thread.
/// </summary>
public sealed class WorkThreadEventSequencer
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, long> _nextByThreadId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the next sequence number for a work thread.
    /// </summary>
    /// <param name="threadId">The work-thread identifier.</param>
    /// <returns>The next 1-based sequence number.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="threadId"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public long Next(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        lock (_gate)
        {
            _nextByThreadId.TryGetValue(threadId, out var current);
            var next = checked(current + 1);
            _nextByThreadId[threadId] = next;
            return next;
        }
    }

    /// <summary>
    /// Resets sequence state for a work thread, typically after actor cleanup.
    /// </summary>
    /// <param name="threadId">The work-thread identifier.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="threadId"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public void Reset(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        lock (_gate)
        {
            _nextByThreadId.Remove(threadId);
        }
    }
}
