using System.Text.Json;

namespace CodeAlta.Agent.Runtime.Compaction;

// 模块功能：对话压缩摘要器，负责将会话历史浓缩为结构化 Markdown 摘要，支持分块、收缩及超大锚点归约
internal sealed class AgentCompactionSummarizer(IAgentCompactionSummaryExecutor executor)
{
    private const int RecursiveChunkPassLimit = 4;

    private const string SummarySystemPromptTemplate =
        """
        You are the CodeAlta compaction summarizer.

        Summarize the supplied conversation state for future continuation by another model call.
        Do not continue the conversation. Do not answer the user's task. Do not invent work.

        Return only markdown with exactly these top-level sections in this order:
        ## Objective
        ## Active User Request
        ## Constraints
        ## Progress
        ### Done
        ### In Progress
        ### Blocked
        ## Decisions
        ## Next Steps
        ## Critical Context
        ## Relevant Files

        Preserve exact file paths, identifiers, tool names, and critical error text when present.
        Keep the summary concise, continuation-oriented, and optimized for replay.
        When a previous summary is provided, update it by preserving still-relevant facts and retiring stale implementation detail; do not append indefinitely.
        Keep Done milestone-level rather than changelog-like. Keep exact commit hashes only when the next agent must reference them.
        Prefer current state, unresolved blockers, verification status, next steps, and active files over exhaustive historical progress.
        Replace stale file lists with current dirty, modified, read, or otherwise active files when newer file activity is provided.
        """;

    private const string ShrinkSystemPromptTemplate =
        """
        You are the CodeAlta compaction summary shrinker.

        Rewrite the supplied compaction checkpoint into a smaller continuation handoff.
        Do not continue the conversation. Do not answer the user's task. Do not invent work.

        Return only markdown with exactly these top-level sections in this order:
        ## Objective
        ## Active User Request
        ## Constraints
        ## Progress
        ### Done
        ### In Progress
        ### Blocked
        ## Decisions
        ## Next Steps
        ## Critical Context
        ## Relevant Files

        Preserve only continuation-critical facts: active objective, explicit constraints, current state, unresolved blockers, next steps, current verification status, active files, exact paths, identifiers, commands, and critical error text.
        Retire stale completed details, old exploratory file lists, old commit hashes unless needed next, and archival narration.
        Prefer compact bullets over prose.
        """;

    private const string OversizedAnchorSystemPromptTemplate =
        """
        You are the CodeAlta oversized-anchor reducer.

        Distill the supplied latest user input into a compact continuation anchor for later compaction.
        Do not answer the user's request. Do not continue the conversation.

        Preserve exact file paths, identifiers, commands, numbered requirements, and critical error text when present.
        Prefer bullets over prose. Keep only continuation-critical details.

        Return only markdown with exactly these top-level sections in this order:
        ## Task
        ## Explicit Requirements
        ## Files and Identifiers
        ## Exact Literals and Errors

        When a previous anchor synopsis is provided, update it rather than rewriting from scratch.
        """;

    private readonly IAgentCompactionSummaryExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    // 函数功能：对给定的会话历史执行完整摘要流程，提取文件活动并在必要时处理超大锚点，返回压缩结果
    public async Task<AgentCompactionResult> SummarizeAsync(
        ModelProviderId ProviderId,
        ModelProviderRuntimeDescriptor provider,
        string sessionId,
        string? modelId,
        AgentModelInfo? modelInfo,
        string? workingDirectory,
        AgentSessionState state,
        AgentCompactionPreparation preparation,
        IReadOnlyList<AgentEvent> history,
        string? latestUserRequest,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(history);

        var settings = provider.Compaction ?? AgentCompactionSettings.Default;
        var fileActivity = ExtractFileActivity(history);
        var modelVisibleFileActivity = BudgetFileActivityForSummary(fileActivity, latestUserRequest, settings, maxOutputTokens);
        string? oversizedAnchorSynopsis = null;
        var oversizedAnchorInvocationCount = 0;
        if (preparation.OversizedAnchorMessage is not null)
        {
            (oversizedAnchorSynopsis, oversizedAnchorInvocationCount) = await ReduceOversizedAnchorAsync(
                    ProviderId,
                    provider,
                    sessionId,
                    modelId,
                    modelInfo,
                    workingDirectory,
                    state,
                    preparation.OversizedAnchorMessage,
                    settings,
                    maxOutputTokens,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var result = await SummarizePreparationAsync(
                ProviderId,
                provider,
                sessionId,
                modelId,
                modelInfo,
                workingDirectory,
                state,
                preparation,
                latestUserRequest,
                maxOutputTokens,
                settings,
                fileActivity,
                modelVisibleFileActivity,
                oversizedAnchorSynopsis,
                oversizedAnchorInvocationCount,
                currentPass: 1,
                cancellationToken)
            .ConfigureAwait(false);
        return result with
        {
            ReadFiles = fileActivity.ReadFiles,
            ModifiedFiles = fileActivity.ModifiedFiles,
            ModelVisibleReadFileCount = modelVisibleFileActivity.ReadFiles.Count,
            ModelVisibleModifiedFileCount = modelVisibleFileActivity.ModifiedFiles.Count,
        };
    }

    // 函数功能：将已有摘要收缩至目标 Token 数，重写为更紧凑的续接摘要，保留关键续接信息
    public async Task<AgentCompactionResult> ShrinkSummaryAsync(
        ModelProviderId ProviderId,
        ModelProviderRuntimeDescriptor provider,
        string sessionId,
        string? modelId,
        AgentModelInfo? modelInfo,
        string? workingDirectory,
        AgentSessionState state,
        AgentCompactionResult result,
        string? latestUserRequest,
        long checkpointTargetTokens,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(result);

        var settings = provider.Compaction ?? AgentCompactionSettings.Default;
        var completeFileActivity = new FileActivity(result.ReadFiles, result.ModifiedFiles);
        var modelVisibleFileActivity = BudgetFileActivityForSummary(completeFileActivity, latestUserRequest, settings, maxOutputTokens);
        var userMessage = BuildShrinkRequestBody(result.Summary, latestUserRequest, modelVisibleFileActivity, checkpointTargetTokens);
        var response = await ExecuteSummaryRequestAsync(
                ProviderId,
                provider,
                sessionId,
                modelId,
                modelInfo,
                workingDirectory,
                state,
                CreateShrinkSystemPrompt(maxOutputTokens, checkpointTargetTokens),
                userMessage,
                Math.Max(maxOutputTokens, 1),
                cancellationToken)
            .ConfigureAwait(false);

        var normalizedSummary = NormalizeSummary(
            response.Summary,
            latestUserRequest,
            result.Summary,
            modelVisibleFileActivity,
            maxOutputTokens);
        ValidateSummaryShape(normalizedSummary);

        return result with
        {
            Summary = normalizedSummary,
            SummaryCallCount = result.SummaryCallCount + 1,
            SummaryPromptInputTokens = result.SummaryPromptInputTokens + AgentTokenEstimator.EstimateTextTokens(userMessage),
            SummaryMaxOutputTokens = maxOutputTokens,
            ModelVisibleReadFileCount = modelVisibleFileActivity.ReadFiles.Count,
            ModelVisibleModifiedFileCount = modelVisibleFileActivity.ModifiedFiles.Count,
        };
    }

    // 函数功能：对单个 preparation 执行摘要，若输入超限则降级为分块流程；返回包含统计信息的 AgentCompactionResult
    private async Task<AgentCompactionResult> SummarizePreparationAsync(
        ModelProviderId ProviderId,
        ModelProviderRuntimeDescriptor provider,
        string sessionId,
        string? modelId,
        AgentModelInfo? modelInfo,
        string? workingDirectory,
        AgentSessionState state,
        AgentCompactionPreparation preparation,
        string? latestUserRequest,
        int maxOutputTokens,
        AgentCompactionSettings settings,
        FileActivity fileActivity,
        FileActivity modelVisibleFileActivity,
        string? oversizedAnchorSynopsis,
        int additionalSummaryCallCount,
        int currentPass,
        CancellationToken cancellationToken)
    {
        var serialization = AgentCompactionSerializer.BuildSummaryRequestBody(
            preparation,
            latestUserRequest,
            modelVisibleFileActivity.ReadFiles,
            modelVisibleFileActivity.ModifiedFiles,
            settings,
            oversizedAnchorSynopsis,
            preparation.OversizedAnchorMessage is not null);

        var chunks = GetChunksIfNeeded(
            preparation,
            latestUserRequest,
            modelVisibleFileActivity,
            settings,
            modelInfo,
            oversizedAnchorSynopsis,
            maxOutputTokens);

        var summaryInputLimit = GetSummaryInputLimit(modelInfo, settings, maxOutputTokens);
        if (summaryInputLimit is > 0 &&
            serialization.EstimatedInputTokens > summaryInputLimit.Value &&
            currentPass < RecursiveChunkPassLimit &&
            chunks.Count > 1)
        {
            return await SummarizeChunkedAsync(
                    ProviderId,
                    provider,
                    sessionId,
                    modelId,
                    modelInfo,
                    workingDirectory,
                    state,
                    preparation,
                    latestUserRequest,
                    maxOutputTokens,
                    settings,
                    fileActivity,
                    modelVisibleFileActivity,
                    oversizedAnchorSynopsis,
                    additionalSummaryCallCount,
                    currentPass,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var response = await ExecuteSummaryRequestAsync(
                ProviderId,
                provider,
                sessionId,
                modelId,
                modelInfo,
                workingDirectory,
                state,
                CreateSystemPrompt(maxOutputTokens),
                serialization.UserMessage,
                maxOutputTokens,
                cancellationToken)
            .ConfigureAwait(false);
        var normalizedSummary = NormalizeSummary(
            response.Summary,
            latestUserRequest,
            preparation.PreviousSummary,
            modelVisibleFileActivity,
            maxOutputTokens);
        ValidateSummaryShape(normalizedSummary);

        return new AgentCompactionResult(
            Summary: normalizedSummary,
            AnchorContentId: preparation.AnchorContentId,
            IsSplitTurn: preparation.IsSplitTurn,
            OversizedAnchorReduced: serialization.Statistics.ReducedOversizedAnchor,
            TokensBefore: preparation.TokensBefore.Tokens,
            TokensAfter: null,
            MessagesSummarized: preparation.MessagesToSummarize.Count,
            ChunkCount: 1,
            SummaryCallCount: additionalSummaryCallCount + 1,
            SummaryMaxOutputTokens: maxOutputTokens,
            SummaryPromptInputTokens: serialization.EstimatedInputTokens,
            SummaryPromptIncludedMessages: serialization.IncludedMessageCount,
            SummaryPromptTotalMessages: serialization.TotalMessageCount,
            CompressionRatio: null,
            SerializerStatistics: serialization.Statistics,
            ReadFiles: fileActivity.ReadFiles,
            ModifiedFiles: fileActivity.ModifiedFiles,
            ModelVisibleReadFileCount: modelVisibleFileActivity.ReadFiles.Count,
            ModelVisibleModifiedFileCount: modelVisibleFileActivity.ModifiedFiles.Count);
    }

    // 函数功能：将待摘要消息分块递归摘要，最终合并保留前后缀的摘要，并汇总统计数据
    private async Task<AgentCompactionResult> SummarizeChunkedAsync(
        ModelProviderId ProviderId,
        ModelProviderRuntimeDescriptor provider,
        string sessionId,
        string? modelId,
        AgentModelInfo? modelInfo,
        string? workingDirectory,
        AgentSessionState state,
        AgentCompactionPreparation preparation,
        string? latestUserRequest,
        int maxOutputTokens,
        AgentCompactionSettings settings,
        FileActivity fileActivity,
        FileActivity modelVisibleFileActivity,
        string? oversizedAnchorSynopsis,
        int additionalSummaryCallCount,
        int currentPass,
        CancellationToken cancellationToken)
    {
        var chunks = GetChunksIfNeeded(preparation, latestUserRequest, modelVisibleFileActivity, settings, modelInfo, oversizedAnchorSynopsis, maxOutputTokens);
        if (chunks.Count <= 1)
        {
            chunks = [preparation.MessagesToSummarize];
        }

        var rollingSummary = preparation.PreviousSummary;
        var aggregatedStatistics = new AgentCompactionSerializerStatistics(
            OmittedToolResultCount: 0,
            OmittedReasoningCount: 0,
            OmittedAttachmentCount: 0,
            DroppedMessageCount: 0,
            SerializedToolResultCharacters: 0,
            SerializedReasoningCharacters: 0,
            ReducedOversizedAnchor: false);
        AgentCompactionResult? finalResult = null;
        var totalChunkCount = 0;
        var totalSummaryCallCount = additionalSummaryCallCount;
        long totalSummaryPromptInputTokens = 0;
        var totalSummaryPromptIncludedMessages = 0;
        var totalSummaryPromptMessages = 0;

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunkPreparation = preparation with
            {
                MessagesToSummarize = chunks[index],
                TurnPrefixMessages = [],
                MessagesToKeep = [],
                PreviousSummary = rollingSummary,
            };

            var chunkResult = await SummarizePreparationAsync(
                    ProviderId,
                    provider,
                    sessionId,
                    modelId,
                    modelInfo,
                    workingDirectory,
                    state,
                    chunkPreparation,
                    latestUserRequest,
                    maxOutputTokens,
                    settings,
                    fileActivity,
                    modelVisibleFileActivity,
                    oversizedAnchorSynopsis,
                    additionalSummaryCallCount: 0,
                    currentPass + 1,
                    cancellationToken)
                .ConfigureAwait(false);

            rollingSummary = chunkResult.Summary;
            aggregatedStatistics = MergeStatistics(aggregatedStatistics, chunkResult.SerializerStatistics);
            totalChunkCount += chunkResult.ChunkCount;
            totalSummaryCallCount += chunkResult.SummaryCallCount;
            totalSummaryPromptInputTokens += chunkResult.SummaryPromptInputTokens;
            totalSummaryPromptIncludedMessages += chunkResult.SummaryPromptIncludedMessages;
            totalSummaryPromptMessages += chunkResult.SummaryPromptTotalMessages;
            finalResult = chunkResult;
        }

        if (finalResult is not null &&
            (preparation.TurnPrefixMessages.Count > 0 || preparation.MessagesToKeep.Count > 0))
        {
            var mergePreparation = preparation with
            {
                MessagesToSummarize = [],
                PreviousSummary = rollingSummary,
            };

            var mergeResult = await SummarizePreparationAsync(
                    ProviderId,
                    provider,
                    sessionId,
                    modelId,
                    modelInfo,
                    workingDirectory,
                    state,
                    mergePreparation,
                    latestUserRequest,
                    maxOutputTokens,
                    settings,
                    fileActivity,
                    modelVisibleFileActivity,
                    oversizedAnchorSynopsis,
                    additionalSummaryCallCount: 0,
                    currentPass + 1,
                    cancellationToken)
                .ConfigureAwait(false);

            rollingSummary = mergeResult.Summary;
            aggregatedStatistics = MergeStatistics(aggregatedStatistics, mergeResult.SerializerStatistics);
            totalChunkCount += mergeResult.ChunkCount;
            totalSummaryCallCount += mergeResult.SummaryCallCount;
            totalSummaryPromptInputTokens += mergeResult.SummaryPromptInputTokens;
            totalSummaryPromptIncludedMessages += mergeResult.SummaryPromptIncludedMessages;
            totalSummaryPromptMessages += mergeResult.SummaryPromptTotalMessages;
            finalResult = mergeResult;
        }

        return finalResult is null
            ? throw new InvalidOperationException("Chunked compaction did not produce a final summary.")
            : finalResult with
            {
                ChunkCount = Math.Max(totalChunkCount, chunks.Count),
                SummaryCallCount = Math.Max(totalSummaryCallCount, 1),
                SummaryPromptInputTokens = totalSummaryPromptInputTokens,
                SummaryPromptIncludedMessages = totalSummaryPromptIncludedMessages,
                SummaryPromptTotalMessages = totalSummaryPromptMessages,
                SerializerStatistics = aggregatedStatistics,
            };
    }

    // 函数功能：根据输入限制将消息列表拆分为分块，若无需拆分则返回原列表
    private IReadOnlyList<IReadOnlyList<AgentConversationMessage>> GetChunksIfNeeded(
        AgentCompactionPreparation preparation,
        string? latestUserRequest,
        FileActivity modelVisibleFileActivity,
        AgentCompactionSettings settings,
        AgentModelInfo? modelInfo,
        string? oversizedAnchorSynopsis,
        int maxOutputTokens)
        => AgentCompactionChunker.CreateChunks(
            preparation.MessagesToSummarize,
            (int)Math.Clamp(GetSummaryInputLimit(modelInfo, settings, maxOutputTokens) ?? long.MaxValue, 1L, int.MaxValue),
            chunkMessages => AgentCompactionSerializer.BuildSummaryRequestBody(
                    preparation with
                    {
                        MessagesToSummarize = chunkMessages,
                        TurnPrefixMessages = [],
                        MessagesToKeep = [],
                    },
                    latestUserRequest,
                    modelVisibleFileActivity.ReadFiles,
                    modelVisibleFileActivity.ModifiedFiles,
                    settings,
                    oversizedAnchorSynopsis,
                    preparation.OversizedAnchorMessage is not null)
                .EstimatedInputTokens);

    // 函数功能：计算摘要请求的最大输入 Token 限制，综合考虑上下文窗口与输出预算
    private static long? GetSummaryInputLimit(AgentModelInfo? modelInfo, AgentCompactionSettings settings, int maxOutputTokens)
    {
        var budget = AgentTokenBudgetResolver.Resolve(modelInfo, settings);
        var inputLimit = budget.InputContextLimit;
        if (budget.TotalContextEnvelope is > 0)
        {
            var envelopeInputLimit = Math.Max(budget.TotalContextEnvelope.Value - Math.Max(maxOutputTokens, 0), 1L);
            inputLimit = inputLimit is > 0
                ? Math.Min(inputLimit.Value, envelopeInputLimit)
                : envelopeInputLimit;
        }

        return inputLimit;
    }

    // 函数功能：将超大锚点消息序列化后提交给归约流程，返回摘要文本及实际调用次数
    private async Task<(string Synopsis, int InvocationCount)> ReduceOversizedAnchorAsync(
        ModelProviderId ProviderId,
        ModelProviderRuntimeDescriptor provider,
        string sessionId,
        string? modelId,
        AgentModelInfo? modelInfo,
        string? workingDirectory,
        AgentSessionState state,
        AgentConversationMessage oversizedAnchorMessage,
        AgentCompactionSettings settings,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var serializedAnchor = SerializeOversizedAnchorMessage(oversizedAnchorMessage);
        if (string.IsNullOrWhiteSpace(serializedAnchor))
        {
            throw new InvalidOperationException("The oversized latest user message could not be reduced because it had no serializable content.");
        }

        return await ReduceOversizedAnchorTextAsync(
                ProviderId,
                provider,
                sessionId,
                modelId,
                modelInfo,
                workingDirectory,
                state,
                serializedAnchor,
                previousSynopsis: null,
                settings,
                Math.Max(maxOutputTokens / 2, 1),
                currentPass: 1,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：递归归约超大锚点文本（超限时分块处理），调用 LLM 生成结构化 synopsis 并归一化
    private async Task<(string Synopsis, int InvocationCount)> ReduceOversizedAnchorTextAsync(
        ModelProviderId ProviderId,
        ModelProviderRuntimeDescriptor provider,
        string sessionId,
        string? modelId,
        AgentModelInfo? modelInfo,
        string? workingDirectory,
        AgentSessionState state,
        string serializedAnchor,
        string? previousSynopsis,
        AgentCompactionSettings settings,
        int maxOutputTokens,
        int currentPass,
        CancellationToken cancellationToken)
    {
        var requestBody = BuildOversizedAnchorRequestBody(serializedAnchor, previousSynopsis);
        var requestTokens = AgentTokenEstimator.EstimateTextTokens(requestBody);
        var summaryInputLimit = GetSummaryInputLimit(modelInfo, settings, maxOutputTokens);
        if (summaryInputLimit is > 0 &&
            requestTokens > summaryInputLimit.Value &&
            currentPass < RecursiveChunkPassLimit)
        {
            var overheadTokens = AgentTokenEstimator.EstimateTextTokens(BuildOversizedAnchorRequestBody(string.Empty, previousSynopsis));
            var availableChunkTokens = Math.Max((int)Math.Min(summaryInputLimit.Value, int.MaxValue) - (int)overheadTokens, 32);
            var chunkTexts = SplitTextByBudget(serializedAnchor, Math.Max(availableChunkTokens * 4, 128));
            if (chunkTexts.Count <= 1 && serializedAnchor.Length > 1)
            {
                chunkTexts = SplitTextByBudget(serializedAnchor, Math.Max(serializedAnchor.Length / 2, 64));
            }

            if (chunkTexts.Count > 1)
            {
                var rollingSynopsis = previousSynopsis;
                var totalInvocations = 0;
                foreach (var chunkText in chunkTexts)
                {
                    var (chunkSynopsis, invocationCount) = await ReduceOversizedAnchorTextAsync(
                            ProviderId,
                            provider,
                            sessionId,
                            modelId,
                            modelInfo,
                            workingDirectory,
                            state,
                            chunkText,
                            rollingSynopsis,
                            settings,
                            maxOutputTokens,
                            currentPass + 1,
                            cancellationToken)
                        .ConfigureAwait(false);
                    rollingSynopsis = chunkSynopsis;
                    totalInvocations += invocationCount;
                }

                return (rollingSynopsis ?? throw new InvalidOperationException("Oversized-anchor reduction did not produce a synopsis."), totalInvocations);
            }
        }

        var response = await ExecuteSummaryRequestAsync(
                ProviderId,
                provider,
                sessionId,
                modelId,
                modelInfo,
                workingDirectory,
                state,
                CreateOversizedAnchorSystemPrompt(maxOutputTokens),
                requestBody,
                maxOutputTokens,
                cancellationToken)
            .ConfigureAwait(false);
        var normalizedSynopsis = NormalizeOversizedAnchorSynopsis(response.Summary, serializedAnchor, previousSynopsis);
        return (normalizedSynopsis, 1);
    }

    // 函数功能：组装并提交摘要请求至执行器，返回 LLM 的摘要响应
    private async Task<AgentCompactionSummaryResponse> ExecuteSummaryRequestAsync(
        ModelProviderId ProviderId,
        ModelProviderRuntimeDescriptor provider,
        string sessionId,
        string? modelId,
        AgentModelInfo? modelInfo,
        string? workingDirectory,
        AgentSessionState state,
        string systemMessage,
        string userMessage,
        int maxOutputTokens,
        CancellationToken cancellationToken)
        => await _executor.ExecuteAsync(
                new AgentCompactionSummaryRequest(
                    ProviderId: ProviderId,
                    Provider: provider,
                    SessionId: sessionId,
                    ModelId: modelId,
                    ModelInfo: modelInfo,
                    WorkingDirectory: workingDirectory,
                    State: state,
                    SystemMessage: systemMessage,
                    UserMessage: userMessage,
                    MaxOutputTokens: maxOutputTokens),
                cancellationToken)
            .ConfigureAwait(false);

    // 函数功能：校验摘要是否非空且包含所有必需章节，不合规则抛出 InvalidOperationException
    private static void ValidateSummaryShape(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException("The compaction summarizer returned an empty summary.");
        }

        if (!HasRequiredSummarySections(summary))
        {
            throw new InvalidOperationException("The compaction summarizer returned a malformed structured summary.");
        }
    }

    // 函数功能：归一化摘要内容，确保所有必需章节存在；若模型输出缺失章节则从历史摘要或原文中补全
    private static string NormalizeSummary(
        string summary,
        string? latestUserRequest,
        string? previousSummary,
        FileActivity fileActivity,
        int maxOutputTokens)
    {
        var trimmedSummary = summary.Trim();

        if (HasRequiredSummarySections(trimmedSummary))
        {
            return trimmedSummary;
        }

        var currentSections = ParseMarkdownSections(trimmedSummary);
        var previousSections = string.IsNullOrWhiteSpace(previousSummary)
            ? null
            : ParseMarkdownSections(previousSummary);

        var objective = FirstNonEmpty(
            GetSection(currentSections, "## Objective"),
            GetSection(previousSections, "## Objective"),
            ExtractLeadParagraph(trimmedSummary, 320),
            "Continue the conversation safely.");
        var activeUserRequest = FirstNonEmpty(
            NormalizeMultiline(latestUserRequest, 640),
            GetSection(currentSections, "## Active User Request"),
            GetSection(previousSections, "## Active User Request"),
            "- Not explicitly captured.");
        var constraints = FirstNonEmpty(
            GetSection(currentSections, "## Constraints"),
            GetSection(previousSections, "## Constraints"),
            "- None explicitly captured.");
        var done = FirstNonEmpty(
            GetSection(currentSections, "### Done"),
            GetSection(previousSections, "### Done"),
            "- None recorded.");
        var inProgress = FirstNonEmpty(
            GetSection(currentSections, "### In Progress"),
            GetSection(previousSections, "### In Progress"),
            "- None recorded.");
        var blocked = FirstNonEmpty(
            GetSection(currentSections, "### Blocked"),
            GetSection(previousSections, "### Blocked"),
            "- None recorded.");
        var decisions = FirstNonEmpty(
            GetSection(currentSections, "## Decisions"),
            GetSection(previousSections, "## Decisions"),
            "- None recorded.");
        var nextSteps = FirstNonEmpty(
            GetSection(currentSections, "## Next Steps"),
            GetSection(previousSections, "## Next Steps"),
            "- Resume from the latest retained context.");
        var relevantFiles = FirstNonEmpty(
            GetSection(currentSections, "## Relevant Files"),
            BuildRelevantFilesSection(fileActivity),
            GetSection(previousSections, "## Relevant Files"),
            "- None tracked.");

        var criticalContext = FirstNonEmpty(
            GetSection(currentSections, "## Critical Context"),
            GetSection(previousSections, "## Critical Context"),
            BuildFallbackCriticalContext(trimmedSummary, maxOutputTokens),
            "- Original draft summary was unavailable.");

        var builder = new System.Text.StringBuilder();
        AppendSummarySection(builder, "## Objective", objective);
        AppendSummarySection(builder, "## Active User Request", activeUserRequest);
        AppendSummarySection(builder, "## Constraints", constraints);
        builder.AppendLine("## Progress");
        builder.AppendLine("### Done");
        builder.AppendLine(done);
        builder.AppendLine();
        builder.AppendLine("### In Progress");
        builder.AppendLine(inProgress);
        builder.AppendLine();
        builder.AppendLine("### Blocked");
        builder.AppendLine(blocked);
        builder.AppendLine();
        AppendSummarySection(builder, "## Decisions", decisions);
        AppendSummarySection(builder, "## Next Steps", nextSteps);
        AppendSummarySection(builder, "## Critical Context", criticalContext);
        AppendSummarySection(builder, "## Relevant Files", relevantFiles, includeTrailingBlankLine: false);
        return builder.ToString().Trim();
    }

    // 函数功能：归一化超大锚点摘要，确保包含所有必需章节；缺失时从历史 synopsis 或原文补全
    private static string NormalizeOversizedAnchorSynopsis(
        string synopsis,
        string serializedAnchor,
        string? previousSynopsis)
    {
        var trimmedSynopsis = synopsis.Trim();
        if (HasRequiredOversizedAnchorSynopsisSections(trimmedSynopsis))
        {
            return trimmedSynopsis;
        }

        var currentSections = ParseOversizedAnchorSections(trimmedSynopsis);
        var previousSections = string.IsNullOrWhiteSpace(previousSynopsis)
            ? null
            : ParseOversizedAnchorSections(previousSynopsis);

        var task = FirstNonEmpty(
            GetSection(currentSections, "## Task"),
            GetSection(previousSections, "## Task"),
            ExtractLeadParagraph(serializedAnchor, 480),
            "- Continue from the oversized latest user request.");
        var explicitRequirements = FirstNonEmpty(
            GetSection(currentSections, "## Explicit Requirements"),
            GetSection(previousSections, "## Explicit Requirements"),
            "- Preserve continuation-critical details from the oversized latest user request.");
        var filesAndIdentifiers = FirstNonEmpty(
            GetSection(currentSections, "## Files and Identifiers"),
            GetSection(previousSections, "## Files and Identifiers"),
            "- None explicitly captured.");
        var exactLiteralsAndErrors = FirstNonEmpty(
            GetSection(currentSections, "## Exact Literals and Errors"),
            GetSection(previousSections, "## Exact Literals and Errors"),
            NormalizeMultiline(trimmedSynopsis, 1200),
            "- None explicitly captured.");

        var builder = new System.Text.StringBuilder();
        AppendSummarySection(builder, "## Task", task);
        AppendSummarySection(builder, "## Explicit Requirements", explicitRequirements);
        AppendSummarySection(builder, "## Files and Identifiers", filesAndIdentifiers);
        AppendSummarySection(builder, "## Exact Literals and Errors", exactLiteralsAndErrors, includeTrailingBlankLine: false);
        return builder.ToString().Trim();
    }

    // 函数功能：合并两个序列化统计对象，将所有计数字段相加，布尔标志取逻辑或
    private static AgentCompactionSerializerStatistics MergeStatistics(
        AgentCompactionSerializerStatistics left,
        AgentCompactionSerializerStatistics right)
        => new(
            OmittedToolResultCount: left.OmittedToolResultCount + right.OmittedToolResultCount,
            OmittedReasoningCount: left.OmittedReasoningCount + right.OmittedReasoningCount,
            OmittedAttachmentCount: left.OmittedAttachmentCount + right.OmittedAttachmentCount,
            DroppedMessageCount: left.DroppedMessageCount + right.DroppedMessageCount,
            SerializedToolResultCharacters: left.SerializedToolResultCharacters + right.SerializedToolResultCharacters,
            SerializedReasoningCharacters: left.SerializedReasoningCharacters + right.SerializedReasoningCharacters,
            ReducedOversizedAnchor: left.ReducedOversizedAnchor || right.ReducedOversizedAnchor,
            TotalToolCallCount: left.TotalToolCallCount + right.TotalToolCallCount,
            SerializedToolCallCount: left.SerializedToolCallCount + right.SerializedToolCallCount,
            CollapsedToolCallCount: left.CollapsedToolCallCount + right.CollapsedToolCallCount,
            TotalToolResultCount: left.TotalToolResultCount + right.TotalToolResultCount,
            SerializedToolResultCount: left.SerializedToolResultCount + right.SerializedToolResultCount,
            SerializedToolResultExcerptCount: left.SerializedToolResultExcerptCount + right.SerializedToolResultExcerptCount,
            TotalReasoningCount: left.TotalReasoningCount + right.TotalReasoningCount,
            SerializedReasoningCount: left.SerializedReasoningCount + right.SerializedReasoningCount,
            TotalAttachmentCount: left.TotalAttachmentCount + right.TotalAttachmentCount,
            SerializedAttachmentCount: left.SerializedAttachmentCount + right.SerializedAttachmentCount);

    // 函数功能：从事件历史中逆序提取工具调用产生的文件读写路径，去重后返回 FileActivity
    private static FileActivity ExtractFileActivity(IReadOnlyList<AgentEvent> history)
    {
        var readFiles = new List<string>();
        var modifiedFiles = new List<string>();
        var seenReadFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenModifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var activity in history.OfType<AgentActivityEvent>().Reverse())
        {
            if (activity.Kind is not AgentActivityKind.ToolCall || activity.Details is not { } details)
            {
                continue;
            }

            AddPaths(details, "modifiedFiles", modifiedFiles, seenModifiedFiles);
            AddPaths(details, "readFiles", readFiles, seenReadFiles);
        }

        return new FileActivity(readFiles, modifiedFiles);
    }

    // 函数功能：按 Token 预算裁剪文件活动列表，优先保留在最新用户请求中明确提及的文件
    private static FileActivity BudgetFileActivityForSummary(
        FileActivity fileActivity,
        string? latestUserRequest,
        AgentCompactionSettings settings,
        int maxOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(fileActivity);
        ArgumentNullException.ThrowIfNull(settings);

        if (fileActivity.ReadFiles.Count == 0 && fileActivity.ModifiedFiles.Count == 0)
        {
            return fileActivity;
        }

        var share = settings.FileContextShareOfSummaryTarget >= 0
            ? Math.Min(settings.FileContextShareOfSummaryTarget, AgentCompactionSettings.MaxFileContextShareOfSummaryTarget)
            : AgentCompactionSettings.DefaultFileContextShareOfSummaryTarget;
        if (share <= 0)
        {
            return new FileActivity([], []);
        }

        var budgetTokens = Math.Max((long)Math.Floor(Math.Max(maxOutputTokens, 1) * share), 1L);
        var latestRequest = latestUserRequest ?? string.Empty;
        var modified = OrderPathsForSummary(fileActivity.ModifiedFiles, latestRequest).ToArray();
        var modifiedSet = new HashSet<string>(modified, StringComparer.OrdinalIgnoreCase);
        var read = OrderPathsForSummary(
                fileActivity.ReadFiles.Where(path => !modifiedSet.Contains(path)),
                latestRequest)
            .ToArray();

        var selectedModified = new List<string>();
        var selectedRead = new List<string>();

        foreach (var path in modified)
        {
            var candidateModified = selectedModified.Concat([path]).ToArray();
            var candidate = new FileActivity(selectedRead, candidateModified);
            var candidateRendered = BuildRelevantFilesSection(candidate);
            if (AgentTokenEstimator.EstimateTextTokens(candidateRendered) > budgetTokens && selectedModified.Count + selectedRead.Count > 0)
            {
                break;
            }

            selectedModified.Add(path);
        }

        foreach (var path in read)
        {
            var candidateRead = selectedRead.Concat([path]).ToArray();
            var candidate = new FileActivity(candidateRead, selectedModified);
            var candidateRendered = BuildRelevantFilesSection(candidate);
            if (AgentTokenEstimator.EstimateTextTokens(candidateRendered) > budgetTokens && selectedModified.Count + selectedRead.Count > 0)
            {
                break;
            }

            selectedRead.Add(path);
        }

        return new FileActivity(selectedRead, selectedModified);
    }

    // 函数功能：对路径列表排序，将用户请求中提及的路径置前，其余按原始顺序保留
    private static IEnumerable<string> OrderPathsForSummary(IEnumerable<string> paths, string latestUserRequest)
    {
        return paths
            .Select((path, index) => new
            {
                Path = path,
                Index = index,
                Mentioned = IsPathMentioned(latestUserRequest, path),
            })
            .OrderByDescending(static item => item.Mentioned)
            .ThenBy(static item => item.Index)
            .Select(static item => item.Path);
    }

    // 函数功能：判断路径或其文件名是否出现在最新用户请求文本中（大小写不敏感）
    private static bool IsPathMentioned(string latestUserRequest, string path)
    {
        if (string.IsNullOrWhiteSpace(latestUserRequest) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (latestUserRequest.Contains(path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var fileName = System.IO.Path.GetFileName(path);
        return !string.IsNullOrWhiteSpace(fileName) &&
            latestUserRequest.Contains(fileName, StringComparison.OrdinalIgnoreCase);
    }

    // 函数功能：从 JSON 工具调用详情中读取指定属性的路径数组，去重后追加到目标集合
    private static void AddPaths(
        JsonElement details,
        string propertyName,
        ICollection<string> target,
        ISet<string> seenPaths)
    {
        if (!details.TryGetProperty(propertyName, out var property) || property.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in property.EnumerateArray())
        {
            var path = item.GetString();
            if (!string.IsNullOrWhiteSpace(path) && seenPaths.Add(path))
            {
                target.Add(path);
            }
        }
    }

    // 函数功能：生成摘要系统提示，在模板基础上附加输出 Token 上限约束
    private static string CreateSystemPrompt(int maxOutputTokens)
        => $"""
            {SummarySystemPromptTemplate}

            Keep the output under roughly {maxOutputTokens} tokens.
            """;

    // 函数功能：生成收缩系统提示，指定目标 Token 数及最大输出 Token 上限
    private static string CreateShrinkSystemPrompt(int maxOutputTokens, long checkpointTargetTokens)
        => $"""
            {ShrinkSystemPromptTemplate}

            Target roughly {checkpointTargetTokens} tokens when possible and never exceed roughly {maxOutputTokens} tokens.
            """;

    // 函数功能：生成超大锚点归约系统提示，在模板基础上附加输出 Token 上限约束
    private static string CreateOversizedAnchorSystemPrompt(int maxOutputTokens)
        => $"""
            {OversizedAnchorSystemPromptTemplate}

            Keep the output under roughly {maxOutputTokens} tokens.
            """;

    // 函数功能：构建收缩请求的 XML 请求体，包含目标 Token 数、当前摘要及相关文件信息
    private static string BuildShrinkRequestBody(
        string summary,
        string? latestUserRequest,
        FileActivity modelVisibleFileActivity,
        long checkpointTargetTokens)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("""<codealta-compaction-shrink-request version="1">""");
        AppendTag(builder, "target-tokens", checkpointTargetTokens.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendTag(builder, "active-user-request", latestUserRequest?.Trim());
        AppendTag(builder, "relevant-files", BuildRelevantFilesSection(modelVisibleFileActivity));
        AppendTag(builder, "current-summary", summary);
        builder.Append("""</codealta-compaction-shrink-request>""");
        return builder.ToString();
    }

    // 函数功能：向 StringBuilder 追加一个 XML 标签块，值经过 XML 转义处理
    private static void AppendTag(System.Text.StringBuilder builder, string tagName, string? value)
    {
        builder.Append('<').Append(tagName).AppendLine(">");
        builder.AppendLine(System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty);
        builder.Append("</").Append(tagName).AppendLine(">");
    }

    // 函数功能：检查摘要字符串是否包含所有必需的 Markdown 章节标题
    private static bool HasRequiredSummarySections(string summary)
        => summary.Contains("## Objective", StringComparison.Ordinal) &&
           summary.Contains("## Active User Request", StringComparison.Ordinal) &&
           summary.Contains("## Constraints", StringComparison.Ordinal) &&
           summary.Contains("## Progress", StringComparison.Ordinal) &&
           summary.Contains("### Done", StringComparison.Ordinal) &&
           summary.Contains("### In Progress", StringComparison.Ordinal) &&
           summary.Contains("### Blocked", StringComparison.Ordinal) &&
           summary.Contains("## Decisions", StringComparison.Ordinal) &&
           summary.Contains("## Next Steps", StringComparison.Ordinal) &&
           summary.Contains("## Critical Context", StringComparison.Ordinal) &&
           summary.Contains("## Relevant Files", StringComparison.Ordinal);

    // 函数功能：检查超大锚点摘要是否包含所有必需的 Markdown 章节标题
    private static bool HasRequiredOversizedAnchorSynopsisSections(string synopsis)
        => synopsis.Contains("## Task", StringComparison.Ordinal) &&
           synopsis.Contains("## Explicit Requirements", StringComparison.Ordinal) &&
           synopsis.Contains("## Files and Identifiers", StringComparison.Ordinal) &&
           synopsis.Contains("## Exact Literals and Errors", StringComparison.Ordinal);

    // 函数功能：将摘要 Markdown 文本解析为章节标题到内容的字典，仅识别受支持的标题
    private static Dictionary<string, string> ParseMarkdownSections(string summary)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentHeading = null;
        var builder = new System.Text.StringBuilder();

        foreach (var rawLine in summary.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.TrimEnd();
            if (IsSupportedSummaryHeading(line))
            {
                FlushSection(sections, currentHeading, builder);
                currentHeading = line.Trim();
                builder.Clear();
                continue;
            }

            if (currentHeading is not null)
            {
                builder.AppendLine(line);
            }
        }

        FlushSection(sections, currentHeading, builder);
        return sections;

        static void FlushSection(IDictionary<string, string> sections, string? heading, System.Text.StringBuilder content)
        {
            if (heading is null)
            {
                return;
            }

            var text = content.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                sections[heading] = text;
            }
        }
    }

    // 函数功能：将超大锚点摘要文本解析为章节标题到内容的字典，仅识别受支持的标题
    private static Dictionary<string, string> ParseOversizedAnchorSections(string synopsis)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentHeading = null;
        var builder = new System.Text.StringBuilder();

        foreach (var rawLine in synopsis.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.TrimEnd();
            if (IsSupportedOversizedAnchorHeading(line))
            {
                FlushSection(sections, currentHeading, builder);
                currentHeading = line.Trim();
                builder.Clear();
                continue;
            }

            if (currentHeading is not null)
            {
                builder.AppendLine(line);
            }
        }

        FlushSection(sections, currentHeading, builder);
        return sections;

        static void FlushSection(IDictionary<string, string> sections, string? heading, System.Text.StringBuilder content)
        {
            if (heading is null)
            {
                return;
            }

            var text = content.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                sections[heading] = text;
            }
        }
    }

    // 函数功能：判断给定行是否为摘要格式中受支持的 Markdown 章节标题
    private static bool IsSupportedSummaryHeading(string line)
        => line is "## Objective"
            or "## Active User Request"
            or "## Constraints"
            or "## Progress"
            or "### Done"
            or "### In Progress"
            or "### Blocked"
            or "## Decisions"
            or "## Next Steps"
            or "## Critical Context"
            or "## Relevant Files";

    // 函数功能：判断给定行是否为超大锚点摘要格式中受支持的 Markdown 章节标题
    private static bool IsSupportedOversizedAnchorHeading(string line)
        => line is "## Task"
            or "## Explicit Requirements"
            or "## Files and Identifiers"
            or "## Exact Literals and Errors";

    // 函数功能：从章节字典中取出指定标题的内容，字典为空或内容空白时返回 null
    private static string? GetSection(IReadOnlyDictionary<string, string>? sections, string heading)
        => sections is not null && sections.TryGetValue(heading, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    // 函数功能：从候选字符串数组中返回第一个非空白字符串，全部为空时返回 string.Empty
    private static string FirstNonEmpty(params string?[] candidates)
        => candidates.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim()
           ?? string.Empty;

    // 函数功能：提取文本首段（遇到句号或换行截断），并限制在 maxCharacters 字符内
    private static string ExtractLeadParagraph(string text, int maxCharacters)
    {
        var normalized = NormalizeMultiline(text, maxCharacters);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var breakIndex = normalized.IndexOfAny(['.', '\n']);
        if (breakIndex > 0 && breakIndex < maxCharacters / 2)
        {
            return normalized[..(breakIndex + 1)].Trim();
        }

        return normalized;
    }

    // 函数功能：将多行文本去空白行后合并为单行，超过 maxCharacters 时截断并加省略号
    private static string NormalizeMultiline(string? text, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        var normalized = string.Join(Environment.NewLine, lines);
        if (normalized.Length <= maxCharacters)
        {
            return normalized;
        }

        return normalized[..Math.Max(maxCharacters - 3, 1)].TrimEnd() + "...";
    }

    // 函数功能：将文件活动（已修改/已读取）格式化为 Markdown 列表字符串，用于摘要中的相关文件章节
    private static string BuildRelevantFilesSection(FileActivity fileActivity)
    {
        var lines = new List<string>();
        foreach (var path in fileActivity.ModifiedFiles)
        {
            lines.Add($"- Modified: {path}");
        }

        foreach (var path in fileActivity.ReadFiles.Where(path => !fileActivity.ModifiedFiles.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            lines.Add($"- Read: {path}");
        }

        return lines.Count == 0 ? "- None tracked." : string.Join(Environment.NewLine, lines);
    }

    // 函数功能：当摘要缺少 Critical Context 章节时，从原始草稿摘要截取一段作为兜底内容
    private static string BuildFallbackCriticalContext(string summary, int maxOutputTokens)
    {
        var maxCharacters = Math.Max(Math.Min(maxOutputTokens * 6, 2400), 480);
        var normalized = NormalizeMultiline(summary, maxCharacters);
        return string.IsNullOrWhiteSpace(normalized)
            ? "- Original draft summary was unavailable."
            : $"- Original draft summary:{Environment.NewLine}{normalized}";
    }

    // 函数功能：向 StringBuilder 追加一个摘要章节（标题+内容），可选是否在末尾追加空行
    private static void AppendSummarySection(
        System.Text.StringBuilder builder,
        string heading,
        string content,
        bool includeTrailingBlankLine = true)
    {
        builder.AppendLine(heading);
        builder.AppendLine(string.IsNullOrWhiteSpace(content) ? "- None recorded." : content.Trim());
        if (includeTrailingBlankLine)
        {
            builder.AppendLine();
        }
    }

    // 函数功能：构建超大锚点归约请求的 XML 请求体，包含模式（初始/更新）、历史摘要及用户消息内容
    private static string BuildOversizedAnchorRequestBody(string serializedAnchor, string? previousSynopsis)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("""<codealta-oversized-anchor-request version="1">""");
        AppendTag(builder, "mode", string.IsNullOrWhiteSpace(previousSynopsis) ? "initial" : "update");
        if (!string.IsNullOrWhiteSpace(previousSynopsis))
        {
            AppendTag(builder, "previous-anchor-synopsis", previousSynopsis);
        }

        AppendTag(builder, "latest-user-message", serializedAnchor);
        builder.Append("""</codealta-oversized-anchor-request>""");
        return builder.ToString();
    }

    // 函数功能：将用户消息各部分（文本、URI 附件、内联数据）序列化为纯文本，用于锚点归约请求
    private static string SerializeOversizedAnchorMessage(AgentConversationMessage message)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case AgentMessagePart.Text text when !string.IsNullOrWhiteSpace(text.Value):
                    builder.AppendLine($"[User] {text.Value.Trim()}");
                    break;
                case AgentMessagePart.Uri uri:
                    builder.AppendLine($"[Attachment] {uri.Name ?? uri.MediaType ?? "uri"}: {uri.Value}");
                    break;
                case AgentMessagePart.Data data:
                    builder.AppendLine($"[Attachment] inline {data.Name ?? data.MediaType}; base64 omitted ({data.Base64Data.Length} chars)");
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    // 函数功能：按字符预算将文本拆分为多块，尽量在换行或空白处断开，返回分块列表
    private static IReadOnlyList<string> SplitTextByBudget(string text, int maxChunkCharacters)
    {
        if (string.IsNullOrWhiteSpace(text) || maxChunkCharacters <= 0 || text.Length <= maxChunkCharacters)
        {
            return [text];
        }

        var chunks = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(maxChunkCharacters, text.Length - start);
            var end = start + length;
            if (end < text.Length)
            {
                var breakIndex = text.LastIndexOfAny(['\n', '\r', ' ', '\t'], end - 1, length);
                if (breakIndex > start + (length / 2))
                {
                    end = breakIndex + 1;
                }
            }

            var chunk = text[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            start = end;
        }

        return chunks;
    }

    // 类型：记录当前会话中已读取与已修改的文件路径列表
    private sealed record FileActivity(IReadOnlyList<string> ReadFiles, IReadOnlyList<string> ModifiedFiles);
}
