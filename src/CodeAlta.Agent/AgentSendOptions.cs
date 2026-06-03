namespace CodeAlta.Agent;

// 模块功能：定义在 Session 中启动新运行所需的发送选项，包括输入内容和可选的 Ask 标识符
/// <summary>
/// Options for starting a new run in a session.
/// </summary>
public sealed class AgentSendOptions
{
    // 说明：本次运行要发送的用户输入
    /// <summary>
    /// Gets or initializes the input to send.
    /// </summary>
    public required AgentInput Input { get; init; }

    // 说明：与本次用户提示关联的 Ask 标识符（可选）
    /// <summary>
    /// Gets the optional ask identifier associated with this user prompt.
    /// </summary>
    public string? AskId { get; init; }
}
