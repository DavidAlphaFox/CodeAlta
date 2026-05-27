using CodeAlta.Agent;
using CodeAlta.Agent.LocalRuntime;

namespace CodeAlta.Presentation.Threads;

internal sealed record ThreadInfoReport(
    string ThreadTitle,
    string ProviderName,
    string ThreadId,
    string WorkingDirectory,
    string? ModelName,
    AgentReasoningEffort? ReasoningEffort,
    DateTimeOffset CreatedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUpdatedAt,
    TimeSpan Elapsed,
    int? UserMessageCount,
    int? AssistantMessageCount,
    ThreadInfoStorageLocation? StorageLocation,
    IReadOnlyList<ThreadInfoFact> ProviderFacts,
    IReadOnlyList<LocalAgentLoadedSkillState> LoadedSkills);

internal sealed record ThreadInfoStorageLocation(
    string Path,
    ThreadInfoStorageKind Kind,
    long? SizeBytes = null);

internal sealed record ThreadInfoFact(
    string Label,
    string Value);

internal enum ThreadInfoStorageKind
{
    File,
    Directory,
    MissingPath,
}
