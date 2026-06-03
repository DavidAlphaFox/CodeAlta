namespace CodeAlta.Agent;

// 模块功能：定义支持推理的模型所使用的推理强度级别枚举
/// <summary>
/// Specifies the reasoning effort level for models that support it.
/// </summary>
public enum AgentReasoningEffort
{
    // 低推理强度
    /// <summary>
    /// Low reasoning effort.
    /// </summary>
    Low = 0,

    // 中推理强度
    /// <summary>
    /// Medium reasoning effort.
    /// </summary>
    Medium = 1,

    // 高推理强度
    /// <summary>
    /// High reasoning effort.
    /// </summary>
    High = 2,

    // 超高推理强度
    /// <summary>
    /// Extra-high reasoning effort.
    /// </summary>
    XHigh = 3,

    // 不使用额外推理
    /// <summary>
    /// No additional reasoning effort.
    /// </summary>
    None = 4,

    // 最低推理强度
    /// <summary>
    /// Minimal reasoning effort.
    /// </summary>
    Minimal = 5,
}
