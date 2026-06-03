namespace CodeAlta.Agent.Runtime;

// 模块功能：定义 Agent 事件的持久化模式枚举
internal enum AgentEventPersistenceMode
{
    // 仅保留在内存中，不持久化到磁盘
    TransientOnly,
    // 持久化到规范事件日志（磁盘）
    DurableCanonical,
}
