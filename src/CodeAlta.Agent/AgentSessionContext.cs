namespace CodeAlta.Agent;

// 模块功能：记录 Agent 会话的仓库与目录上下文信息（工作目录、git 根目录、仓库标识、分支名）
/// <summary>
/// Captures repository and directory context for a session.
/// </summary>
/// <param name="Cwd">The session working directory.</param>
/// <param name="GitRoot">The git repository root, if any.</param>
/// <param name="Repository">The GitHub repository in "owner/repo" format, if known.</param>
/// <param name="Branch">The current git branch, if known.</param>
public sealed record AgentSessionContext(
    string? Cwd = null,
    string? GitRoot = null,
    string? Repository = null,
    string? Branch = null);

