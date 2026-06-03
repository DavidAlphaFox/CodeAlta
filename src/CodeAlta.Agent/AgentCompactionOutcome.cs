namespace CodeAlta.Agent;

// 模块功能：定义手动压缩操作的结果记录及其提供者接口
/// <summary>
/// Represents the outcome of a manual compaction operation when the provider returns a synchronous result.
/// </summary>
/// <param name="Success">Whether compaction completed successfully.</param>
/// <param name="Message">Optional user-visible summary.</param>
/// <param name="MessagesRemoved">Optional number of removed messages.</param>
/// <param name="TokensRemoved">Optional number of removed tokens.</param>
/// <param name="PreCompactionTokens">Optional token count before compaction.</param>
/// <param name="PostCompactionTokens">Optional token count after compaction.</param>
public sealed record AgentCompactionOutcome(
    bool Success,
    string? Message = null,
    int? MessagesRemoved = null,
    long? TokensRemoved = null,
    long? PreCompactionTokens = null,
    long? PostCompactionTokens = null);

// 类型：可选 Session 能力接口，用于公开同步手动压缩结果
/// <summary>
/// Optional session capability that exposes a synchronous manual compaction outcome.
/// </summary>
public interface IAgentCompactionOutcomeProvider
{
    // 函数功能：触发手动压缩并在提供商同步完成时返回结果；异步完成时返回 null
    /// <summary>
    /// Triggers manual compaction and returns a provider-reported outcome when available.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A compaction outcome when the provider completes the operation synchronously; otherwise <see langword="null" />.
    /// </returns>
    Task<AgentCompactionOutcome?> CompactWithOutcomeAsync(CancellationToken cancellationToken = default);
}
