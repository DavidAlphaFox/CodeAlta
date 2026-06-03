namespace CodeAlta.Agent;

// 模块功能：定义模型提供商注册表接口，支持列举提供商、查询描述符及获取/创建运行时
/// <summary>
/// Provides read-only model-provider listing and runtime lookup.
/// </summary>
public interface IModelProviderRegistry
{
    // 函数功能：列举已配置的模型提供商；includeDisabled 控制是否包含禁用的提供商，返回提供商描述符列表
    /// <summary>
    /// Lists configured model providers.
    /// </summary>
    /// <param name="includeDisabled">Whether disabled providers should be included.</param>
    /// <returns>The configured provider descriptors.</returns>
    IReadOnlyList<ModelProviderDescriptor> ListProviders(bool includeDisabled = false);

    // 函数功能：尝试按 providerId 获取已配置的提供商描述符，找到时返回 true 并通过 descriptor 输出结果
    /// <summary>
    /// Attempts to get a configured provider descriptor.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="descriptor">The descriptor when found.</param>
    /// <returns><see langword="true" /> when the provider exists; otherwise <see langword="false" />.</returns>
    bool TryGetProvider(ModelProviderId providerId, out ModelProviderDescriptor descriptor);

    // 函数功能：按 providerId 获取或创建提供商运行时实例（异步），providerId 不存在时抛出 KeyNotFoundException
    /// <summary>
    /// Gets or creates the runtime for a configured provider.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The provider runtime.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerId" /> is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when <paramref name="providerId" /> is not registered.</exception>
    ValueTask<IModelProviderRuntime> GetOrCreateRuntimeAsync(
        ModelProviderId providerId,
        CancellationToken cancellationToken = default);
}
