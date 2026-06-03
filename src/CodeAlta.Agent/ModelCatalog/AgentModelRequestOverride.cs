namespace CodeAlta.Agent.ModelCatalog;

// 模块功能：描述当模型 ID 匹配时应用于请求的自定义 HTTP 头和请求体覆盖配置
/// <summary>
/// Describes request-level customizations that apply when a model id matches a configured model request override.
/// </summary>
public sealed class AgentModelRequestOverride
{
    // 说明：为匹配模型请求附加的静态 HTTP 头
    /// <summary>
    /// Gets or sets static HTTP headers added for matching model requests.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    // 说明：在应用本模型头之前需移除的提供商/默认头名称列表（必要认证头不可移除）
    /// <summary>
    /// Gets or sets provider/default headers to remove before this model's headers are applied.
    /// Required authentication headers cannot be removed.
    /// </summary>
    public IReadOnlyList<string>? RemoveHeaders { get; set; }

    // 说明：为匹配模型请求附加的 OpenAI 兼容请求体字段
    /// <summary>
    /// Gets or sets OpenAI-compatible request-body fields added for matching model requests.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ExtraBody { get; set; }

    // 说明：在应用本模型请求体字段之前需移除的提供商/默认字段名称列表
    /// <summary>
    /// Gets or sets provider/default OpenAI-compatible request-body fields to remove before this model's fields are applied.
    /// </summary>
    public IReadOnlyList<string>? RemoveExtraBody { get; set; }
}
