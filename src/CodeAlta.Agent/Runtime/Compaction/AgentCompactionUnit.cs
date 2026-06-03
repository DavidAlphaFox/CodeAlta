namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：定义压缩单元的抽象基类及两种具体实现（单条消息单元、工具交互单元）
// 类型：压缩单元抽象基类，持有来源消息列表并公开会话角色
internal abstract record AgentCompactionUnit(IReadOnlyList<AgentConversationMessage> SourceMessages)
{
    public abstract AgentConversationRole Role { get; }
}

// 类型：表示单条对话消息的压缩单元
internal sealed record AgentCompactionMessageUnit(AgentConversationMessage Message)
    : AgentCompactionUnit([Message])
{
    public override AgentConversationRole Role => Message.Role;
}

// 类型：表示一次工具调用交互（助手消息 + 工具结果消息集合）的压缩单元，支持折叠去重
internal sealed record AgentCompactionToolInteractionUnit(
    AgentConversationMessage AssistantMessage,
    IReadOnlyList<AgentConversationMessage> ToolMessages,
    int RepeatCount = 1,
    bool IsCollapsed = false,
    string? CollapseKey = null)
    : AgentCompactionUnit([AssistantMessage, .. ToolMessages])
{
    public override AgentConversationRole Role => AssistantMessage.Role;

    // 说明：从助手消息中提取的工具调用列表
    public IReadOnlyList<AgentMessagePart.ToolCall> ToolCalls { get; }
        = AssistantMessage.Parts.OfType<AgentMessagePart.ToolCall>().ToArray();

    // 说明：从工具消息中提取的工具结果列表
    public IReadOnlyList<AgentMessagePart.ToolResult> ToolResults { get; }
        = ToolMessages.SelectMany(static message => message.Parts.OfType<AgentMessagePart.ToolResult>()).ToArray();
}
