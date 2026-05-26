namespace CodeAlta.Agent;

/// <summary>
/// Defines provider-independent access to persisted CodeAlta agent sessions.
/// </summary>
public interface IAgentSessionStore
{
    /// <summary>
    /// Lists persisted sessions from the configured sessions root.
    /// </summary>
    /// <param name="filter">Optional session filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Session metadata streamed from the store.</returns>
    IAsyncEnumerable<AgentSessionMetadata> ListSessionsAsync(
        AgentSessionListFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets persisted session metadata by session identifier.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session metadata when present; otherwise <see langword="null" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task<AgentSessionMetadata?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads canonical session events by session identifier.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The canonical session events when the session exists; otherwise an empty list.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task<IReadOnlyList<AgentEvent>> ReadEventsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a persisted session by session identifier.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true" /> when the session existed and was deleted; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task<bool> DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
