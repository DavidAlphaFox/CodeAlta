using CodeAlta.Agent;
using CodeAlta.Catalog;

namespace CodeAlta.Models;

internal readonly record struct PromptSessionId
{
    public PromptSessionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value;
}

internal readonly record struct ThreadDraftId
{
    public ThreadDraftId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value;
}

internal readonly record struct ModelProviderId
{
    public ModelProviderId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value;
}

internal abstract record ShellThreadRef
{
    private ShellThreadRef()
    {
    }

    public sealed record Draft : ShellThreadRef
    {
        public Draft(ThreadDraftId draftId)
        {
            if (draftId.IsEmpty)
            {
                throw new ArgumentException("Thread draft id cannot be empty.", nameof(draftId));
            }

            DraftId = draftId;
        }

        public ThreadDraftId DraftId { get; init; }
    }

    public sealed record Running : ShellThreadRef
    {
        public Running(string threadId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
            ThreadId = threadId;
        }

        public string ThreadId { get; init; }
    }
}

internal sealed record PromptSessionBinding
{
    public PromptSessionBinding(
        PromptSessionId promptSessionId,
        ProjectId projectId,
        ShellThreadRef thread,
        ModelProviderId modelProviderId,
        string? modelId = null,
        AgentReasoningEffort? reasoningEffort = null)
    {
        if (promptSessionId.IsEmpty)
        {
            throw new ArgumentException("Prompt session id cannot be empty.", nameof(promptSessionId));
        }

        if (projectId == default)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        }

        ArgumentNullException.ThrowIfNull(thread);

        if (modelProviderId.IsEmpty)
        {
            throw new ArgumentException("Model provider id cannot be empty.", nameof(modelProviderId));
        }

        PromptSessionId = promptSessionId;
        ProjectId = projectId;
        Thread = thread;
        ModelProviderId = modelProviderId;
        ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId;
        ReasoningEffort = reasoningEffort;
    }

    public PromptSessionId PromptSessionId { get; init; }

    public ProjectId ProjectId { get; init; }

    public ShellThreadRef Thread { get; init; }

    public ModelProviderId ModelProviderId { get; init; }

    public string? ModelId { get; init; }

    public AgentReasoningEffort? ReasoningEffort { get; init; }
}
