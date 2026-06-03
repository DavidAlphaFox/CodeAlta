namespace CodeAlta.Agent;

// 模块功能：定义与提供者无关的持久化 CodeAlta Agent 会话访问接口，支持列举、查询、读取事件与删除操作
/// <summary>
/// Defines provider-independent access to persisted CodeAlta agent sessions.
/// </summary>
public interface IAgentSessionStore
{
    // 函数功能：从已配置的会话根目录流式返回已持久化的会话元数据
    /// <summary>
    /// Lists persisted sessions from the configured sessions root.
    /// </summary>
    /// <param name="filter">Optional session filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Session metadata streamed from the store.</returns>
    IAsyncEnumerable<AgentSessionMetadata> ListSessionsAsync(
        AgentSessionListFilter? filter = null,
        CancellationToken cancellationToken = default);

    // 函数功能：按 sessionId 获取已持久化的会话元数据，不存在时返回 null
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

    // 函数功能：按 sessionId 读取已持久化的规范事件列表，会话不存在时返回空列表
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

    // 函数功能：按 sessionId 删除已持久化会话；返回是否实际存在并已删除
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
