namespace CodeAlta.Agent;

// 模块功能：表示文本文档中的某个位置（行号和字符索引均从 1 开始计数）
/// <summary>
/// Represents a position in a text document.
/// </summary>
/// <param name="Line">The 1-based line number.</param>
/// <param name="Character">The 1-based character index.</param>
public sealed record AgentPosition(int Line, int Character);

