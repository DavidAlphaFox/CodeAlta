using System.Text.Json;

namespace CodeAlta.Agent.LocalRuntime.Compaction;

internal sealed class LocalAgentCompactionSummarizer(ILocalAgentCompactionSummaryExecutor executor)
{
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
        When a previous summary is provided, update it rather than rewriting from scratch.
        """;

    private readonly ILocalAgentCompactionSummaryExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public async Task<LocalAgentCompactionResult> SummarizeAsync(
        AgentBackendId backendId,
        LocalAgentProviderDescriptor provider,
        string sessionId,
        string? modelId,
        AgentModelInfo? modelInfo,
        string? workingDirectory,
        LocalAgentSessionState state,
        LocalAgentCompactionPreparation preparation,
        IReadOnlyList<AgentEvent> history,
        string? latestUserRequest,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(history);

        var fileActivity = ExtractFileActivity(history);
        var serialization = LocalAgentCompactionSerializer.BuildSummaryRequestBody(
            preparation,
            latestUserRequest,
            fileActivity.ReadFiles,
            fileActivity.ModifiedFiles,
            provider.Compaction ?? LocalAgentCompactionSettings.Default);
        var response = await _executor.ExecuteAsync(
                new LocalAgentCompactionSummaryRequest(
                    BackendId: backendId,
                    Provider: provider,
                    SessionId: sessionId,
                    ModelId: modelId,
                    ModelInfo: modelInfo,
                    WorkingDirectory: workingDirectory,
                    State: state,
                    SystemMessage: CreateSystemPrompt(maxOutputTokens),
                    UserMessage: serialization.UserMessage,
                    MaxOutputTokens: maxOutputTokens),
                cancellationToken)
            .ConfigureAwait(false);
        ValidateSummary(response.Summary, maxOutputTokens);

        return new LocalAgentCompactionResult(
            Summary: response.Summary,
            AnchorContentId: preparation.AnchorContentId,
            IsSplitTurn: preparation.IsSplitTurn,
            OversizedAnchorReduced: serialization.Statistics.ReducedOversizedAnchor,
            TokensBefore: preparation.TokensBefore.Tokens,
            TokensAfter: null,
            MessagesSummarized: preparation.MessagesToSummarize.Count,
            ChunkCount: 1,
            CompressionRatio: null,
            SerializerStatistics: serialization.Statistics,
            ReadFiles: fileActivity.ReadFiles,
            ModifiedFiles: fileActivity.ModifiedFiles);
    }

    private static void ValidateSummary(string summary, int maxOutputTokens)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException("The compaction summarizer returned an empty summary.");
        }

        var estimatedTokens = LocalAgentTokenEstimator.EstimateTextTokens(summary);
        if (estimatedTokens > Math.Max(maxOutputTokens * 2L, 256L))
        {
            throw new InvalidOperationException("The compaction summarizer returned a summary that exceeds the configured checkpoint budget.");
        }

        if (!summary.Contains("## Objective", StringComparison.Ordinal) ||
            !summary.Contains("## Active User Request", StringComparison.Ordinal) ||
            !summary.Contains("## Relevant Files", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The compaction summarizer returned a malformed structured summary.");
        }
    }

    private static FileActivity ExtractFileActivity(IReadOnlyList<AgentEvent> history)
    {
        var readFiles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var modifiedFiles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var activity in history.OfType<AgentActivityEvent>())
        {
            if (activity.Kind is not AgentActivityKind.ToolCall || activity.Details is not { } details)
            {
                continue;
            }

            AddPaths(details, "readFiles", readFiles);
            AddPaths(details, "modifiedFiles", modifiedFiles);
        }

        return new FileActivity([.. readFiles], [.. modifiedFiles]);
    }

    private static void AddPaths(JsonElement details, string propertyName, ISet<string> target)
    {
        if (!details.TryGetProperty(propertyName, out var property) || property.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in property.EnumerateArray())
        {
            var path = item.GetString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                target.Add(path);
            }
        }
    }

    private static string CreateSystemPrompt(int maxOutputTokens)
        => $"""
            {SummarySystemPromptTemplate}

            Keep the output under roughly {maxOutputTokens} tokens.
            """;

    private sealed record FileActivity(IReadOnlyList<string> ReadFiles, IReadOnlyList<string> ModifiedFiles);
}
