namespace CodeAlta.Agent;

// 模块功能：代表单个对话会话，提供发送消息、订阅事件、转向、终止与压缩等会话生命周期操作
/// <summary>
/// Represents a single conversation session.
/// </summary>
public interface IAgentSession : IAsyncDisposable
{
    /// <summary>
    /// Gets the provider/runtime identifier carried by this session.
    /// </summary>
    ModelProviderId ProviderId { get; }

    /// <summary>
    /// Gets the durable CodeAlta session identifier.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// Gets the optional provider/runtime workspace directory for this session.
    /// </summary>
    string? WorkspacePath { get; }

    // 函数功能：以异步流形式返回归一化的 Agent 事件序列
    /// <summary>
    /// Streams normalized agent events.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    IAsyncEnumerable<AgentEvent> StreamEventsAsync(CancellationToken cancellationToken = default);

    // 函数功能：订阅归一化 Agent 事件；返回的 IDisposable 可取消订阅
    /// <summary>
    /// Subscribes to normalized agent events.
    /// </summary>
    /// <param name="handler">The event handler.</param>
    /// <returns>An <see cref="IDisposable"/> that unsubscribes when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is <see langword="null"/>.</exception>
    IDisposable Subscribe(Action<AgentEvent> handler);

    // 函数功能：向会话发送用户输入，返回本次运行的 AgentRunId（如回合 ID 或消息 ID）
    /// <summary>
    /// Sends user input to the session.
    /// </summary>
    /// <param name="options">Send options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The provider/runtime run identifier (e.g. turn id or message id).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    Task<AgentRunId> SendAsync(AgentSendOptions options, CancellationToken cancellationToken = default);

    // 函数功能：在不开启新轮次的情况下向当前活跃运行注入转向输入，返回接受转向的 AgentRunId
    /// <summary>
    /// Steers the currently active run without starting a new one.
    /// </summary>
    /// <param name="options">Steering options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The provider/runtime run identifier that accepted the steering input.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown when the provider/runtime does not support steering.</exception>
    /// <exception cref="InvalidOperationException">Thrown when there is no active run to steer.</exception>
    Task<AgentRunId> SteerAsync(AgentSteerOptions options, CancellationToken cancellationToken = default);

    // 函数功能：尽力中止/取消会话中当前正在进行的工作
    /// <summary>
    /// Aborts/cancels the currently running work in this session (best effort).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task AbortAsync(CancellationToken cancellationToken = default);

    // 函数功能：在提供者/运行时支持时触发手动会话压缩（Compaction）
    /// <summary>
    /// Triggers a manual session compaction when supported by the provider/runtime.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="NotSupportedException">Thrown when the provider/runtime does not support manual compaction.</exception>
    Task CompactAsync(CancellationToken cancellationToken = default);

    // 函数功能：尽力返回会话的已存储历史事件列表
    /// <summary>
    /// Gets the stored history for the session (best effort).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(CancellationToken cancellationToken = default);
}
