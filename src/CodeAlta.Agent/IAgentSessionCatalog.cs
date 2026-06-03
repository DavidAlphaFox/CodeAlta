namespace CodeAlta.Agent;

// 模块功能：提供缓存的、与提供者无关的 CodeAlta Agent 会话目录，支持列举、失效与增删通知
/// <summary>
/// Provides cached, provider-independent discovery of persisted CodeAlta agent sessions.
/// </summary>
public interface IAgentSessionCatalog
{
    // 函数功能：流式返回目录快照中的已持久化会话元数据，按需触发共享加载
    /// <summary>
    /// Lists persisted sessions from the catalog snapshot, starting one shared load when needed.
    /// </summary>
    /// <param name="filter">Optional session filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Session metadata streamed from the catalog snapshot.</returns>
    IAsyncEnumerable<AgentSessionMetadata> ListSessionsAsync(
        AgentSessionListFilter? filter = null,
        CancellationToken cancellationToken = default);

    // 函数功能：使整个目录快照失效，完成后返回
    /// <summary>
    /// Invalidates the whole catalog snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    Task InvalidateAsync(CancellationToken cancellationToken = default);

    // 函数功能：使指定 sessionId 相关的目录缓存项失效
    /// <summary>
    /// Invalidates catalog entries affected by a session identifier.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task InvalidateAsync(string sessionId, CancellationToken cancellationToken = default);

    // 函数功能：通知目录某会话已创建，并使相关缓存失效
    /// <summary>
    /// Notifies the catalog that a session was created and invalidates cached metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task NotifySessionCreatedAsync(string sessionId, CancellationToken cancellationToken = default);

    // 函数功能：通知目录某会话已恢复（Resume），并使相关缓存失效
    /// <summary>
    /// Notifies the catalog that a session was resumed and invalidates cached metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task NotifySessionResumedAsync(string sessionId, CancellationToken cancellationToken = default);

    // 函数功能：通知目录某会话已删除，并使相关缓存失效
    /// <summary>
    /// Notifies the catalog that a session was deleted and invalidates cached metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task NotifySessionDeletedAsync(string sessionId, CancellationToken cancellationToken = default);

    // 函数功能：按 sessionId 删除已持久化会话，并使缓存失效；返回是否实际存在并删除
    /// <summary>
    /// Deletes a persisted session by session identifier and invalidates cached metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true" /> when the session existed and was deleted; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    // 函数功能：通知目录某会话已更新，并使相关缓存失效
    /// <summary>
    /// Notifies the catalog that a session was updated and invalidates cached metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been invalidated.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId" /> is empty.</exception>
    Task NotifySessionUpdatedAsync(string sessionId, CancellationToken cancellationToken = default);
}
