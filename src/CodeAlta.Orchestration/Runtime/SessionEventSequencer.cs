namespace CodeAlta.Orchestration.Runtime;

/// <summary>
/// Assigns monotonically increasing sequence numbers to events for each session view.
/// </summary>
public sealed class SessionEventSequencer
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, long> _nextBySessionId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the next sequence number for a session view.
    /// </summary>
    /// <param name="sessionId">The session-view identifier.</param>
    /// <returns>The next 1-based sequence number.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public long Next(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (_gate)
        {
            _nextBySessionId.TryGetValue(sessionId, out var current);
            var next = checked(current + 1);
            _nextBySessionId[sessionId] = next;
            return next;
        }
    }

    /// <summary>
    /// Resets sequence state for a session view, typically after actor cleanup.
    /// </summary>
    /// <param name="sessionId">The session-view identifier.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public void Reset(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (_gate)
        {
            _nextBySessionId.Remove(sessionId);
        }
    }
}
