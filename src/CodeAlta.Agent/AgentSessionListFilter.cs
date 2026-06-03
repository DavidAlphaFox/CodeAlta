namespace CodeAlta.Agent;

// 模块功能：定义列举 Agent 会话时可用的筛选条件，支持按工作目录、git 根目录、仓库和分支过滤
/// <summary>
/// Filter used when listing sessions.
/// </summary>
/// <param name="Cwd">Filter by exact working directory.</param>
/// <param name="GitRoot">Filter by git root.</param>
/// <param name="Repository">Filter by repository in "owner/repo" format.</param>
/// <param name="Branch">Filter by branch.</param>
public sealed record AgentSessionListFilter(
    string? Cwd = null,
    string? GitRoot = null,
    string? Repository = null,
    string? Branch = null);

