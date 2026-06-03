namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：使用 IModelProviderTurnExecutor 执行单轮会话压缩，从 Assistant 消息中提取摘要文本并返回
internal sealed class AgentTurnExecutorCompactionSummaryExecutor(IModelProviderTurnExecutor turnExecutor)
    : IAgentCompactionSummaryExecutor
{
    private readonly IModelProviderTurnExecutor _turnExecutor = turnExecutor ?? throw new ArgumentNullException(nameof(turnExecutor));

    // 函数功能：执行压缩摘要请求，调用 turnExecutor 发起单轮对话，提取首个非空 Text 部件作为摘要返回
    public async Task<AgentCompactionSummaryResponse> ExecuteAsync(
        AgentCompactionSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _turnExecutor.ExecuteTurnAsync(
                new AgentTurnRequest
                {
                    Provider = request.Provider,
                    ProviderId = request.ProviderId,
                    SessionId = request.SessionId,
                    RunId = new AgentRunId($"compaction-summary:{Guid.CreateVersion7()}"),
                    ModelId = request.ModelId,
                    ModelInfo = request.ModelInfo,
                    WorkingDirectory = request.WorkingDirectory,
                    SystemMessage = request.SystemMessage,
                    DeveloperInstructions = null,
                    ReasoningEffort = null,
                    MaxOutputTokens = request.MaxOutputTokens,
                    Conversation =
                    [
                        new AgentConversationMessage(
                            AgentConversationRole.User,
                            [new AgentMessagePart.Text(request.UserMessage)]),
                    ],
                    Tools = [],
                    State = request.State,
                },
                static (_, _) => ValueTask.CompletedTask,
                cancellationToken)
            .ConfigureAwait(false);

        var summary = response.AssistantMessage.Parts
            .OfType<AgentMessagePart.Text>()
            .Select(static part => part.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        return new AgentCompactionSummaryResponse(summary?.Trim() ?? string.Empty, response.Usage);
    }
}
