namespace CodeAlta.Agent;

// 模块功能：定义向正在运行的 Agent 轮次注入追加输入的选项
/// <summary>
/// Options for steering an already running agent turn.
/// </summary>
public sealed class AgentSteerOptions
{
    // 说明：追加到当前活动运行的输入内容
    /// <summary>
    /// Gets or initializes the input to append to the active run.
    /// </summary>
    public required AgentInput Input { get; init; }

    // 说明：期望的活动运行标识符；指定时仅对匹配的运行生效，省略时由适配器决定
    /// <summary>
    /// Gets or initializes the expected active run identifier.
    /// </summary>
    /// <remarks>
    /// When specified, the adapter should only steer the matching in-flight run.
    /// When omitted, the adapter may use its current active run if the provider supports that behavior.
    /// </remarks>
    public AgentRunId? ExpectedRunId { get; init; }
}
