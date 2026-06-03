using System.Collections.Generic;

namespace CodeAlta.Agent;

// 模块功能：初始化已配置的模型提供者，跟踪其就绪状态，并持有各提供者的模型目录
/// <summary>
/// Initializes configured model providers, records readiness state, and owns per-provider model catalogs.
/// </summary>
public interface IModelProviderInitializationService
{
    /// <summary>
    /// Gets the latest known provider state snapshots.
    /// </summary>
    IReadOnlyList<ModelProviderStateSnapshot> CurrentStates { get; }

    // 函数功能：以异步流形式返回初始化或刷新操作发布的提供者状态变更事件
    /// <summary>
    /// Streams provider state changes published by initialization or refresh operations.
    /// </summary>
    /// <param name="cancellationToken">A token to stop reading changes.</param>
    /// <returns>Provider state changes.</returns>
    IAsyncEnumerable<ModelProviderStateChanged> StreamStateChangesAsync(CancellationToken cancellationToken = default);

    // 函数功能：并发初始化所有已配置提供者，等待全部探针完成或取消后返回
    /// <summary>
    /// Initializes all configured providers independently.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel waiting for initialization.</param>
    /// <returns>A task that completes when all currently configured provider probes have completed or the wait is canceled.</returns>
    Task InitializeAllAsync(CancellationToken cancellationToken = default);

    // 函数功能：刷新指定提供者的状态与模型目录，等待刷新完成或取消后返回
    /// <summary>
    /// Refreshes a single provider state and model catalog.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="cancellationToken">A token to cancel waiting for the refresh.</param>
    /// <returns>A task that completes when the selected provider refresh has completed or the wait is canceled.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerId" /> is empty.</exception>
    Task RefreshProviderAsync(ModelProviderId providerId, CancellationToken cancellationToken = default);

    // 函数功能：获取指定提供者的缓存模型列表，若尚未初始化则触发首次初始化；无可用目录时返回空列表
    /// <summary>
    /// Gets the cached model list for a provider, starting its first initialization when no state exists yet.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="cancellationToken">A token to cancel waiting for first initialization.</param>
    /// <returns>The cached model catalog when available; otherwise an empty list.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerId" /> is empty.</exception>
    Task<IReadOnlyList<AgentModelInfo>> GetModelsAsync(ModelProviderId providerId, CancellationToken cancellationToken = default);
}

// 类型：ModelProviderInitializationService 的配置选项，目前包含提供者探针默认超时
/// <summary>
/// Options for <see cref="ModelProviderInitializationService" />.
/// </summary>
public sealed record ModelProviderInitializationOptions
{
    /// <summary>
    /// Gets the default timeout applied to provider startup and probing.
    /// </summary>
    /// <remarks>
    /// Provider configuration does not yet carry a persisted per-provider probe timeout, so Phase 2 applies this
    /// service-level default consistently until provider configuration grows a targeted timeout setting.
    /// </remarks>
    public TimeSpan DefaultProbeTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

// 类型：已发布的模型提供者状态变更通知，携带最新状态快照
/// <summary>
/// Describes a published model-provider state change.
/// </summary>
/// <param name="State">The latest provider state snapshot.</param>
public sealed record ModelProviderStateChanged(ModelProviderStateSnapshot State);
