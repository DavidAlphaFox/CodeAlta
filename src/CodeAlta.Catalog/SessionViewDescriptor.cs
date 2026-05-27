using System.Text.Json.Serialization;

namespace CodeAlta.Catalog;

/// <summary>
/// Describes a durable work thread.
/// </summary>
public sealed class SessionViewDescriptor
{
    /// <summary>
    /// Gets or sets the stable thread identifier.
    /// </summary>
    [JsonPropertyName("thread_id")]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the thread kind.
    /// </summary>
    [JsonPropertyName("kind")]
    public WorkThreadKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the model provider identifier for the thread.
    /// Serialized as <c>backend_id</c> so existing work-thread front matter remains readable.
    /// </summary>
    [JsonPropertyName("backend_id")]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider key selected for the thread.
    /// </summary>
    [JsonPropertyName("provider_key")]
    public string? ProviderKey { get; set; }

    /// <summary>
    /// Gets or sets the owning project identifier for project threads.
    /// </summary>
    [JsonPropertyName("project_ref")]
    public string? ProjectRef { get; set; }

    /// <summary>
    /// Gets or sets the legacy parent thread identifier for internal thread metadata.
    /// </summary>
    [JsonPropertyName("parent_thread_id")]
    public string? ParentThreadId { get; set; }

    /// <summary>
    /// Gets or sets durable attribution for the actor that created this thread.
    /// </summary>
    [JsonPropertyName("created_by")]
    public AltaActorProvenance? CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets the session working directory.
    /// </summary>
    [JsonPropertyName("working_directory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the thread title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the durable status.
    /// </summary>
    [JsonPropertyName("status")]
    public WorkThreadStatus Status { get; set; } = WorkThreadStatus.Draft;

    /// <summary>
    /// Gets or sets the creation time.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update time.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last active time.
    /// </summary>
    [JsonPropertyName("last_active_at")]
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>
    /// Gets or sets the optional first prompt timestamp.
    /// </summary>
    [JsonPropertyName("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the latest durable summary.
    /// </summary>
    [JsonPropertyName("latest_summary")]
    public string? LatestSummary { get; set; }

    /// <summary>
    /// Gets or sets the thread model identifier when known.
    /// </summary>
    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    /// <summary>
    /// Gets or sets the thread reasoning effort when known.
    /// </summary>
    [JsonPropertyName("reasoning_effort")]
    public CodeAlta.Agent.AgentReasoningEffort? ReasoningEffort { get; set; }

    /// <summary>
    /// Gets or sets the cached number of displayable messages when known.
    /// </summary>
    [JsonPropertyName("message_count")]
    public int? MessageCount { get; set; }

    /// <summary>
    /// Gets the path to the source markdown file when loaded from disk.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Gets or sets the markdown body loaded from disk.
    /// </summary>
    public string? MarkdownBody { get; set; }

    /// <summary>
    /// Gets a value indicating whether the thread can no longer change backends.
    /// </summary>
    public bool IsBackendLocked => StartedAt is not null;

    /// <summary>
    /// Gets the provider key to use for execution.
    /// </summary>
    [JsonIgnore]
    public string ResolvedProviderKey => string.IsNullOrWhiteSpace(ProviderKey) ? ProviderId : ProviderKey;

    /// <summary>
    /// Marks the thread as started.
    /// </summary>
    /// <param name="timestamp">The timestamp to record.</param>
    public void MarkStarted(DateTimeOffset timestamp)
    {
        if (StartedAt is null)
        {
            StartedAt = timestamp;
        }

        UpdatedAt = timestamp;
        LastActiveAt = timestamp;
        if (Status == WorkThreadStatus.Draft)
        {
            Status = WorkThreadStatus.Active;
        }
    }

    /// <summary>
    /// Validates the descriptor.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when required values are missing or invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ThreadId))
        {
            throw new ArgumentException("Thread id is required.", nameof(ThreadId));
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new ArgumentException("Thread title is required.", nameof(Title));
        }

        if (CreatedAt == default)
        {
            throw new ArgumentException("CreatedAt is required.", nameof(CreatedAt));
        }

        if (UpdatedAt == default)
        {
            throw new ArgumentException("UpdatedAt is required.", nameof(UpdatedAt));
        }

        if (LastActiveAt == default)
        {
            throw new ArgumentException("LastActiveAt is required.", nameof(LastActiveAt));
        }

        if (string.IsNullOrWhiteSpace(ProviderId))
        {
            throw new ArgumentException("Backend id is required.", nameof(ProviderId));
        }

        if (string.IsNullOrWhiteSpace(WorkingDirectory))
        {
            throw new ArgumentException("Working directory is required.", nameof(WorkingDirectory));
        }

        if (MessageCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MessageCount), MessageCount, "MessageCount cannot be negative.");
        }

        switch (Kind)
        {
            case WorkThreadKind.GlobalThread:
                if (!string.IsNullOrWhiteSpace(ProjectRef))
                {
                    throw new ArgumentException("Global threads cannot declare a project.", nameof(ProjectRef));
                }

                break;

            case WorkThreadKind.ProjectThread:
                if (!ProjectId.TryParse(ProjectRef, out _))
                {
                    throw new ArgumentException("Project threads require a valid project_ref.", nameof(ProjectRef));
                }

                break;

            case WorkThreadKind.InternalThread:
                break;
        }
    }
}

