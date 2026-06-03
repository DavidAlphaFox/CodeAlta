using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

// 模块功能：定义提供商运行标识符的值类型（如 Codex 轮次 ID 或 Copilot 消息 ID）
/// <summary>
/// Identifies a provider run (e.g. a Codex turn id or Copilot message id).
/// </summary>
/// <param name="Value">The identifier value.</param>
[JsonConverter(typeof(AgentRunIdJsonConverter))]
public readonly record struct AgentRunId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
