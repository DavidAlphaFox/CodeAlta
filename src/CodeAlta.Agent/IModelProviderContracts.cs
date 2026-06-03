using CodeAlta.Agent.Runtime;

namespace CodeAlta.Agent;

// 模块功能：定义模型提供者的适配器、运行时及轮次执行器核心接口契约
/// <summary>
/// Creates runtimes for model provider definitions owned by a host-specific registry builder.
/// </summary>
public interface IModelProviderAdapter
{
    /// <summary>
    /// Gets the provider adapter type, such as <c>openai-chat</c> or <c>anthropic</c>.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Returns whether this adapter can create a runtime for the provider descriptor.
    /// </summary>
    /// <param name="descriptor">The provider descriptor.</param>
    /// <returns><see langword="true" /> when the adapter supports the provider; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor" /> is <see langword="null" />.</exception>
    bool CanCreateRuntime(ModelProviderDescriptor descriptor);

    /// <summary>
    /// Creates a provider runtime for the descriptor.
    /// </summary>
    /// <param name="descriptor">The provider descriptor.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The created provider runtime.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor" /> is <see langword="null" />.</exception>
    ValueTask<IModelProviderRuntime> CreateRuntimeAsync(
        ModelProviderDescriptor descriptor,
        CancellationToken cancellationToken = default);
}

// 模块功能：已初始化的模型提供者运行时，支持启动/停止/探针/轮次执行器创建
/// <summary>
/// Represents an initialized model provider runtime.
/// </summary>
public interface IModelProviderRuntime : IAsyncDisposable
{
    /// <summary>
    /// Gets the provider descriptor.
    /// </summary>
    ModelProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Starts the provider runtime and performs any required lightweight handshake.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the provider runtime and releases runtime resources.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Probes the provider for readiness and model metadata.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<ModelProviderProbeResult> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a turn executor for this provider runtime.
    /// </summary>
    IModelProviderTurnExecutor CreateTurnExecutor();
}

// 类型：可附加到 Agent 会话运行时的提供者运行时，额外提供运行时描述符和模型目录
/// <summary>
/// Represents a provider runtime that can be attached to the agent session runtime.
/// </summary>
public interface IAgentModelProviderRuntime : IModelProviderRuntime
{
    /// <summary>
    /// Gets the provider descriptor used by agent session execution.
    /// </summary>
    ModelProviderRuntimeDescriptor RuntimeDescriptor { get; }

    /// <summary>
    /// Gets the optional provider model catalog used for probing.
    /// </summary>
    IModelProviderModelCatalog? ModelCatalog { get; }

    /// <summary>
    /// Creates the provider registration consumed by the agent session runtime.
    /// </summary>
    /// <returns>The provider registration.</returns>
    AgentRuntimeProviderRegistration CreateProviderRegistration();
}

// 类型：直接拥有 Agent 会话创建能力的提供者运行时
/// <summary>
/// Represents a model-provider runtime that owns agent session creation directly.
/// </summary>
public interface IModelProviderSessionRuntime : IModelProviderRuntime
{
    /// <summary>
    /// Creates an agent session for this provider.
    /// </summary>
    Task<IAgentSession> CreateSessionAsync(AgentSessionCreateOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes an agent session for this provider.
    /// </summary>
    Task<IAgentSession> ResumeSessionAsync(string sessionId, AgentSessionResumeOptions options, CancellationToken cancellationToken = default);
}

// 类型：向模型提供者执行单轮对话请求的执行器接口
/// <summary>
/// Executes turns against a model provider.
/// </summary>
public interface IModelProviderTurnExecutor
{
    /// <summary>
    /// Executes a single assistant turn.
    /// </summary>
    /// <param name="request">The turn request.</param>
    /// <param name="onUpdate">Streaming callback used for best-effort progress projection.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The final assistant turn response.</returns>
    Task<AgentTurnResponse> ExecuteTurnAsync(
        AgentTurnRequest request,
        Func<AgentTurnDelta, CancellationToken, ValueTask> onUpdate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a single assistant turn with a callback for provider session updates.
    /// </summary>
    /// <param name="request">The turn request.</param>
    /// <param name="onUpdate">Streaming callback used for best-effort progress projection.</param>
    /// <param name="onSessionUpdate">Session update callback used for best-effort status projection.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The final assistant turn response.</returns>
    Task<AgentTurnResponse> ExecuteTurnAsync(
        AgentTurnRequest request,
        Func<AgentTurnDelta, CancellationToken, ValueTask> onUpdate,
        Func<AgentTurnSessionUpdate, CancellationToken, ValueTask> onSessionUpdate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onSessionUpdate);
        return ExecuteTurnAsync(request, onUpdate, cancellationToken);
    }
}
