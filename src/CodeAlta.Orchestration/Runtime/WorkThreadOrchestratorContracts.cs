using CodeAlta.Agent;
using CodeAlta.Catalog;

namespace CodeAlta.Orchestration.Runtime;

/// <summary>
/// Identifies the prompt/session context for a headless work-thread command.
/// </summary>
public sealed record WorkThreadCommandContext
{
    /// <summary>Gets the owning project identifier.</summary>
    public required string ProjectId { get; init; }

    /// <summary>Gets the owning project path.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Gets the prompt-session identifier.</summary>
    public required string PromptSessionId { get; init; }

    /// <summary>Gets the selected model-provider identifier.</summary>
    public required string ModelProviderId { get; init; }

    /// <summary>Gets the selected model identifier, when one is selected.</summary>
    public string? ModelId { get; init; }

    /// <summary>Gets the thread-draft identifier, when the command targets an unmaterialized draft.</summary>
    public string? ThreadDraftId { get; init; }

    /// <summary>Gets the durable thread identifier, when the command targets a materialized thread.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Gets backend execution options associated with the command.</summary>
    public SessionExecutionOptions? ExecutionOptions { get; init; }
}

/// <summary>
/// Describes an attachment submitted with a headless work-thread prompt.
/// </summary>
public sealed record WorkThreadPromptAttachment
{
    /// <summary>Gets the stable attachment identifier supplied by the caller.</summary>
    public required string AttachmentId { get; init; }

    /// <summary>Gets the attachment kind. <see cref="WorkThreadPromptAttachmentKind.Auto"/> derives the kind from available metadata.</summary>
    public WorkThreadPromptAttachmentKind Kind { get; init; } = WorkThreadPromptAttachmentKind.Auto;

    /// <summary>Gets the attachment path, when the attachment is file-backed.</summary>
    public string? Path { get; init; }

    /// <summary>Gets the attachment display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets text content for text-backed attachments or selections.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the attachment content type, when known.</summary>
    public string? ContentType { get; init; }

    /// <summary>Gets the attachment bytes, when the caller materialized content in memory.</summary>
    public ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Gets an optional line range for file or directory attachments.</summary>
    public AgentLineRange? LineRange { get; init; }

    /// <summary>Gets an optional selection range for selection attachments.</summary>
    public AgentSelectionRange? SelectionRange { get; init; }
}

/// <summary>
/// Identifies a headless work-thread prompt attachment kind.
/// </summary>
public enum WorkThreadPromptAttachmentKind
{
    /// <summary>Derive the attachment kind from path, content type, and selection metadata.</summary>
    Auto,

    /// <summary>Plain text content.</summary>
    Text,

    /// <summary>A file path attachment.</summary>
    File,

    /// <summary>A directory path attachment.</summary>
    Directory,

    /// <summary>An image path or URL attachment.</summary>
    Image,

    /// <summary>A code/text selection attachment.</summary>
    Selection,

    /// <summary>Metadata for plugin consumers; ignored by agent input materialization.</summary>
    Metadata,
}

/// <summary>
/// Carries approval behavior for headless work-thread commands.
/// </summary>
public sealed record WorkThreadApprovalContext
{
    /// <summary>Gets a value indicating whether tool/permission requests may be auto-approved by policy.</summary>
    public bool AutoApprove { get; init; }

    /// <summary>Gets an optional approval policy identifier.</summary>
    public string? ApprovalPolicyId { get; init; }

    /// <summary>Gets caller-supplied approval metadata.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Requests creation of a prompt draft scoped to a project and model provider.
/// </summary>
public sealed record CreateWorkThreadDraftRequest
{
    /// <summary>Gets the command context.</summary>
    public required WorkThreadCommandContext Context { get; init; }

    /// <summary>Gets the optional draft title.</summary>
    public string? Title { get; init; }
}

/// <summary>
/// Requests materialization or launch of a work thread from a draft/session context.
/// </summary>
public sealed record LaunchWorkThreadRequest
{
    /// <summary>Gets the command context.</summary>
    public required WorkThreadCommandContext Context { get; init; }

    /// <summary>Gets the optional thread title.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the optional first prompt.</summary>
    public string? InitialPrompt { get; init; }

    /// <summary>Gets prompt attachments for the first prompt.</summary>
    public IReadOnlyList<WorkThreadPromptAttachment> Attachments { get; init; } = [];

    /// <summary>Gets approval behavior for the launch.</summary>
    public WorkThreadApprovalContext Approval { get; init; } = new();
}

/// <summary>
/// Requests prompt submission to an existing or draft-backed work thread.
/// </summary>
public sealed record SubmitWorkThreadPromptRequest
{
    /// <summary>Gets the command context.</summary>
    public required WorkThreadCommandContext Context { get; init; }

    /// <summary>Gets the prompt text.</summary>
    public required string Prompt { get; init; }

    /// <summary>Gets a pre-materialized agent input when the caller has already resolved attachments/references.</summary>
    public AgentInput? PreparedInput { get; init; }

    /// <summary>Gets prompt attachments.</summary>
    public IReadOnlyList<WorkThreadPromptAttachment> Attachments { get; init; } = [];

    /// <summary>Gets approval behavior for the prompt.</summary>
    public WorkThreadApprovalContext Approval { get; init; } = new();

    /// <summary>Gets a value indicating whether the prompt should be queued if the target thread is busy.</summary>
    public bool QueueIfBusy { get; init; }
}

/// <summary>
/// Requests steering input for an active work thread run.
/// </summary>
public sealed record SteerWorkThreadRequest
{
    /// <summary>Gets the command context.</summary>
    public required WorkThreadCommandContext Context { get; init; }

    /// <summary>Gets the steering prompt.</summary>
    public required string Prompt { get; init; }

    /// <summary>Gets a pre-materialized agent input when the caller has already resolved attachments/references.</summary>
    public AgentInput? PreparedInput { get; init; }

    /// <summary>Gets steering attachments.</summary>
    public IReadOnlyList<WorkThreadPromptAttachment> Attachments { get; init; } = [];

    /// <summary>Gets approval behavior for the steering prompt.</summary>
    public WorkThreadApprovalContext Approval { get; init; } = new();
}

/// <summary>
/// Requests abort of an active work-thread run.
/// </summary>
public sealed record AbortWorkThreadRequest
{
    /// <summary>Gets the durable thread identifier.</summary>
    public required string ThreadId { get; init; }

    /// <summary>Gets the owning project identifier.</summary>
    public required string ProjectId { get; init; }

    /// <summary>Gets the reason surfaced to diagnostics.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Requests work-thread compaction.
/// </summary>
public sealed record CompactWorkThreadRequest
{
    /// <summary>Gets the command context.</summary>
    public required WorkThreadCommandContext Context { get; init; }

    /// <summary>Gets a value indicating whether the compacted result should be submitted immediately.</summary>
    public bool SubmitAfterCompaction { get; init; }
}

/// <summary>
/// Requests activation of a skill for a work-thread context.
/// </summary>
public sealed record ActivateSkillRequest
{
    /// <summary>Gets the command context.</summary>
    public required WorkThreadCommandContext Context { get; init; }

    /// <summary>Gets the skill name.</summary>
    public required string SkillName { get; init; }
}

/// <summary>
/// Requests queueing a prompt for later work-thread submission.
/// </summary>
public sealed record QueueWorkThreadPromptRequest
{
    /// <summary>Gets the command context.</summary>
    public required WorkThreadCommandContext Context { get; init; }

    /// <summary>Gets the prompt text.</summary>
    public required string Prompt { get; init; }

    /// <summary>Gets prompt attachments.</summary>
    public IReadOnlyList<WorkThreadPromptAttachment> Attachments { get; init; } = [];

    /// <summary>Gets approval behavior for the queued prompt.</summary>
    public WorkThreadApprovalContext Approval { get; init; } = new();
}

/// <summary>
/// Classifies command outcomes returned by the work-thread orchestrator facade.
/// </summary>
public enum WorkThreadCommandOutcomeKind
{
    /// <summary>The command completed successfully.</summary>
    Completed,

    /// <summary>The command submitted work to a running thread.</summary>
    Submitted,

    /// <summary>The command queued work for later processing.</summary>
    Queued,

    /// <summary>The command steered an active run.</summary>
    Steered,

    /// <summary>The command was cancelled by a plugin or caller policy.</summary>
    Cancelled,

    /// <summary>The command was rejected because the target or provider lacks a required capability.</summary>
    Rejected,

    /// <summary>The command failed and the caller should consider restoring the submitted prompt.</summary>
    FailedWithRestoreRecommendation,
}

/// <summary>
/// Immutable snapshot of a durable work-thread descriptor.
/// </summary>
public sealed record SessionViewDescriptorSnapshot
{
    /// <summary>Gets the stable thread identifier.</summary>
    public required string ThreadId { get; init; }

    /// <summary>Gets the thread kind.</summary>
    public WorkThreadKind Kind { get; init; }

    /// <summary>Gets the backend identifier for the thread.</summary>
    public required string BackendId { get; init; }

    /// <summary>Gets the provider key selected for the thread.</summary>
    public string? ProviderKey { get; init; }

    /// <summary>Gets the owning project identifier for project threads.</summary>
    public string? ProjectRef { get; init; }

    /// <summary>Gets the legacy parent thread identifier for internal thread metadata.</summary>
    public string? ParentThreadId { get; init; }

    /// <summary>Gets durable attribution for the actor that created this thread.</summary>
    public AltaActorProvenance? CreatedBy { get; init; }

    /// <summary>Gets the session working directory.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Gets the thread title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the durable status.</summary>
    public WorkThreadStatus Status { get; init; }

    /// <summary>Gets the creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the last update time.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Gets the last active time.</summary>
    public DateTimeOffset LastActiveAt { get; init; }

    /// <summary>Gets the optional first prompt timestamp.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Gets the latest durable summary.</summary>
    public string? LatestSummary { get; init; }

    /// <summary>Gets the cached number of displayable messages when known.</summary>
    public int? MessageCount { get; init; }

    /// <summary>Gets the path to the source markdown file when loaded from disk.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Gets the markdown body loaded from disk.</summary>
    public string? MarkdownBody { get; init; }

    /// <summary>
    /// Creates an immutable snapshot from a mutable work-thread descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor to copy.</param>
    /// <returns>An immutable descriptor snapshot.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is <see langword="null"/>.</exception>
    public static SessionViewDescriptorSnapshot FromDescriptor(SessionViewDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new SessionViewDescriptorSnapshot
        {
            ThreadId = descriptor.ThreadId,
            Kind = descriptor.Kind,
            BackendId = descriptor.BackendId,
            ProviderKey = descriptor.ProviderKey,
            ProjectRef = descriptor.ProjectRef,
            ParentThreadId = descriptor.ParentThreadId,
            CreatedBy = descriptor.CreatedBy,
            WorkingDirectory = descriptor.WorkingDirectory,
            Title = descriptor.Title,
            Status = descriptor.Status,
            CreatedAt = descriptor.CreatedAt,
            UpdatedAt = descriptor.UpdatedAt,
            LastActiveAt = descriptor.LastActiveAt,
            StartedAt = descriptor.StartedAt,
            LatestSummary = descriptor.LatestSummary,
            MessageCount = descriptor.MessageCount,
            SourcePath = descriptor.SourcePath,
            MarkdownBody = descriptor.MarkdownBody,
        };
    }
}

/// <summary>
/// Describes the result of an orchestrator command.
/// </summary>
public sealed record WorkThreadCommandResult
{
    /// <summary>Gets the outcome kind.</summary>
    public required WorkThreadCommandOutcomeKind Outcome { get; init; }

    /// <summary>Gets the immutable durable thread snapshot, when a command materialized or affected a thread.</summary>
    public SessionViewDescriptorSnapshot? Thread { get; init; }

    /// <summary>Gets a user- or log-facing message for the outcome.</summary>
    public string? Message { get; init; }

    /// <summary>Gets the runtime run identifier, when a command submitted or steered a run.</summary>
    public string? RunId { get; init; }

    /// <summary>Gets a value indicating whether the caller should restore the prompt text and attachments.</summary>
    public bool ShouldRestorePrompt { get; init; }
}

/// <summary>
/// Represents a work-thread snapshot returned by the orchestrator facade.
/// </summary>
public sealed record WorkThreadSnapshot
{
    /// <summary>Gets the immutable durable thread snapshot.</summary>
    public required SessionViewDescriptorSnapshot Thread { get; init; }

    /// <summary>Gets a value indicating whether a run is currently active.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets the queued prompt count known to the orchestrator.</summary>
    public int QueuedPromptCount { get; init; }
}

/// <summary>
/// Base type for events emitted by the work-thread orchestrator facade.
/// </summary>
public abstract record WorkThreadOrchestratorEvent
{
    /// <summary>Gets the durable thread identifier associated with the event.</summary>
    public required string ThreadId { get; init; }

    /// <summary>Gets a monotonically increasing sequence number scoped to the thread when known.</summary>
    public long? SequenceNumber { get; init; }
}

/// <summary>
/// Classifies thread lifecycle events emitted by orchestration for runtime observers and plugins.
/// </summary>
public enum WorkThreadLifecycleEventKind
{
    /// <summary>A prompt session was associated with a thread or draft.</summary>
    SessionStarted,

    /// <summary>A durable thread was created or materialized.</summary>
    ThreadMaterialized,

    /// <summary>A backend run was submitted.</summary>
    RunSubmitted,

    /// <summary>A backend run completed successfully.</summary>
    RunCompleted,

    /// <summary>A backend run failed.</summary>
    RunFailed,

    /// <summary>A backend run was aborted.</summary>
    RunAborted,

    /// <summary>The legacy thread view was rekeyed to a new runtime session identifier.</summary>
    SessionRekeyed,
}

/// <summary>
/// Describes a work-thread lifecycle event suitable for plugin and frontend projections.
/// </summary>
public sealed record WorkThreadLifecycleEvent : WorkThreadOrchestratorEvent
{
    /// <summary>Gets the lifecycle event kind.</summary>
    public required WorkThreadLifecycleEventKind Kind { get; init; }

    /// <summary>Gets the prompt-session identifier associated with the event, when known.</summary>
    public string? PromptSessionId { get; init; }

    /// <summary>Gets the run identifier associated with the event, when known.</summary>
    public string? RunId { get; init; }

    /// <summary>Gets the previous thread or session identifier for rekey events.</summary>
    public string? PreviousId { get; init; }

    /// <summary>Gets a diagnostic message for failure or cancellation lifecycle events.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Describes a work-thread queue change event suitable for plugin and frontend projections.
/// </summary>
public sealed record WorkThreadQueueChangedEvent : WorkThreadOrchestratorEvent
{
    /// <summary>Gets the current queued prompt count after the change.</summary>
    public int QueuedPromptCount { get; init; }

    /// <summary>Gets the queue item identifier associated with the change, when known.</summary>
    public string? QueueItemId { get; init; }

    /// <summary>Gets a value indicating whether the event represents enqueueing a prompt.</summary>
    public bool IsEnqueued { get; init; }
}

/// <summary>
/// Exposes headless command/query/event APIs for work-thread orchestration.
/// </summary>
public interface IWorkThreadOrchestrator
{
    /// <summary>Creates a prompt draft.</summary>
    ValueTask<WorkThreadCommandResult> CreateDraftAsync(CreateWorkThreadDraftRequest request, CancellationToken cancellationToken = default);

    /// <summary>Launches or materializes a work thread.</summary>
    ValueTask<WorkThreadCommandResult> LaunchThreadAsync(LaunchWorkThreadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Submits a prompt to a work thread.</summary>
    ValueTask<WorkThreadCommandResult> SubmitPromptAsync(SubmitWorkThreadPromptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Steers an active work-thread run.</summary>
    ValueTask<WorkThreadCommandResult> SteerAsync(SteerWorkThreadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Aborts an active work-thread run.</summary>
    ValueTask<WorkThreadCommandResult> AbortAsync(AbortWorkThreadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Compacts a work thread.</summary>
    ValueTask<WorkThreadCommandResult> CompactAsync(CompactWorkThreadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Activates a skill for a work-thread context.</summary>
    ValueTask<WorkThreadCommandResult> ActivateSkillAsync(ActivateSkillRequest request, CancellationToken cancellationToken = default);

    /// <summary>Queues a prompt for later submission.</summary>
    ValueTask<WorkThreadCommandResult> QueuePromptAsync(QueueWorkThreadPromptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets a snapshot for a durable work thread.</summary>
    ValueTask<WorkThreadSnapshot?> GetThreadSnapshotAsync(string threadId, CancellationToken cancellationToken = default);

    /// <summary>Streams orchestrator events without exposing actor references or actor paths.</summary>
    IAsyncEnumerable<WorkThreadOrchestratorEvent> StreamEventsAsync(CancellationToken cancellationToken = default);
}
