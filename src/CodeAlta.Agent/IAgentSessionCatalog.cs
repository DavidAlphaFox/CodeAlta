namespace CodeAlta.Agent;

/// <summary>
/// Provides cached, provider-independent discovery of persisted CodeAlta agent sessions.
/// </summary>
public interface IAgentSessionCatalog
{
    /// <summary>
    /// Lists persisted sessions from the catalog snapshot, starting one shared load when needed.
    /// </summary>
    /// <param name="filter">Optional session filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Session metadata streamed from the catalog snapshot.</returns>
    IAsyncEnumerable<AgentSessionMetadata> ListSessionsAsync(
        AgentSessionListFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the whole catalog snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    Task InvalidateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates catalog entries affected by a session identifier.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task InvalidateAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the catalog that a session was created and invalidates cached metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task NotifySessionCreatedAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the catalog that a session was resumed and invalidates cached metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task NotifySessionResumedAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the catalog that a session was deleted and invalidates cached metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task NotifySessionDeletedAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the catalog that a session was updated and invalidates cached metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task NotifySessionUpdatedAsync(string sessionId, CancellationToken cancellationToken = default);
}
