namespace CodeAlta.Agent;

// 模块功能：表示由一个或多个输入项组成的用户输入，并提供纯文本输入的静态工厂方法
/// <summary>
/// Represents user input composed of one or more input items.
/// </summary>
/// <param name="Items">The input items.</param>
public sealed record AgentInput(IReadOnlyList<AgentInputItem> Items)
{
    // 函数功能：根据文本字符串创建纯文本 AgentInput；text 为空时抛出 ArgumentNullException
    /// <summary>
    /// Creates a text-only input.
    /// </summary>
    /// <param name="text">The text to send.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
    public static AgentInput Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new AgentInput([new AgentInputItem.Text(text)]);
    }
}

