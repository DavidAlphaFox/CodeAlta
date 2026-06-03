namespace CodeAlta.Agent;

// 模块功能：表示文本文档中的选区范围，由起始位置和结束位置组成
/// <summary>
/// Represents a selection range in a text document.
/// </summary>
/// <param name="Start">The selection start position.</param>
/// <param name="End">The selection end position.</param>
public sealed record AgentSelectionRange(AgentPosition Start, AgentPosition End);

