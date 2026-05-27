using CodeAlta.Agent;
using CodeAlta.Agent.LocalRuntime;
using CodeAlta.Catalog;

namespace CodeAlta.Presentation.Threads;

internal static class ThreadInfoReportBuilder
{
    public static ThreadInfoReport Build(
        SessionViewDescriptor thread,
        string backendName,
        string? modelName,
        AgentReasoningEffort? reasoningEffort,
        AgentSessionMetadata? metadata,
        IReadOnlyList<AgentEvent>? history,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentException.ThrowIfNullOrWhiteSpace(backendName);

        var createdAt = metadata?.CreatedAt ?? thread.CreatedAt;
        var startedAt = thread.StartedAt ?? createdAt;
        var lastUpdatedAt = metadata?.UpdatedAt ?? thread.UpdatedAt;
        var elapsed = now >= startedAt ? now - startedAt : TimeSpan.Zero;

        return new ThreadInfoReport(
            ThreadTitle: thread.Title,
            BackendName: backendName,
            ThreadId: thread.ThreadId,
            WorkingDirectory: thread.WorkingDirectory,
            ModelName: modelName,
            ReasoningEffort: reasoningEffort,
            CreatedAt: createdAt,
            StartedAt: startedAt,
            LastUpdatedAt: lastUpdatedAt,
            Elapsed: elapsed,
            UserMessageCount: CountMessages(history, AgentContentKind.User),
            AssistantMessageCount: CountMessages(history, AgentContentKind.Assistant),
            StorageLocation: ProbeStorageLocation(metadata?.WorkspacePath),
            BackendFacts: BuildBackendFacts(metadata?.Details),
            LoadedSkills: BuildLoadedSkills(history));
    }

    private static int? CountMessages(IReadOnlyList<AgentEvent>? history, AgentContentKind kind)
    {
        if (history is null)
        {
            return null;
        }

        return history.Count(@event => @event is AgentContentCompletedEvent completed && completed.Kind == kind);
    }

    private static ThreadInfoStorageLocation? ProbeStorageLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return new ThreadInfoStorageLocation(
                path,
                ThreadInfoStorageKind.File,
                new FileInfo(path).Length);
        }

        if (Directory.Exists(path))
        {
            return new ThreadInfoStorageLocation(
                path,
                ThreadInfoStorageKind.Directory);
        }

        return new ThreadInfoStorageLocation(
            path,
            ThreadInfoStorageKind.MissingPath);
    }

    private static IReadOnlyList<ThreadInfoFact> BuildBackendFacts(AgentSessionMetadataDetails? details)
    {
        if (details is null)
        {
            return [];
        }

        var facts = new List<ThreadInfoFact>();
        switch (details)
        {
            case CodexSessionMetadataDetails codex:
                AddFact(facts, "Model provider", codex.ModelProvider);
                AddFact(facts, "Source", codex.Source);
                AddFact(facts, "Status", codex.Status);
                AddFact(facts, "Persistence", codex.IsEphemeral ? "Ephemeral" : "Persisted on disk");
                AddFact(facts, "Session name", codex.ThreadName);
                break;

            case CopilotSessionMetadataDetails copilot:
                AddFact(facts, "Remote session", copilot.IsRemote ? "Yes" : "No");
                break;
        }

        return facts;
    }

    private static IReadOnlyList<LocalAgentLoadedSkillState> BuildLoadedSkills(IReadOnlyList<AgentEvent>? history)
    {
        if (history is null)
        {
            return [];
        }

        var loadedSkills = new Dictionary<string, LocalAgentLoadedSkillState>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawEvent in history.OfType<AgentRawEvent>())
        {
            if (!string.Equals(rawEvent.BackendEventType, "local.skillActivation", StringComparison.Ordinal))
            {
                continue;
            }

            var skill = rawEvent.Raw.ToLocalAgentLoadedSkillState();
            if (skill is null || string.IsNullOrWhiteSpace(skill.Name))
            {
                continue;
            }

            loadedSkills[skill.Name] = skill;
        }

        return loadedSkills.Values
            .OrderBy(static skill => skill.ActivatedAt)
            .ThenBy(static skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddFact(List<ThreadInfoFact> facts, string label, string? value)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        facts.Add(new ThreadInfoFact(label, value));
    }
}
