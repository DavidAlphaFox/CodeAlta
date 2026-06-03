namespace CodeAlta.Agent;

// 模块功能：表示文本文档中的行范围（起止行均从 1 开始计数）
/// <summary>
/// Represents a line range in a text document.
/// </summary>
/// <param name="StartLine">The 1-based start line.</param>
/// <param name="EndLine">The 1-based end line.</param>
public sealed record AgentLineRange(int StartLine, int EndLine);

