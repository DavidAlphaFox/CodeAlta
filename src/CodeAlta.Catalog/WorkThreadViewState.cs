using System.Text.Json.Serialization;
using CodeAlta.Agent;

namespace CodeAlta.Catalog;

/// <summary>
/// Describes the machine-local UI state for restoring open work threads.
/// </summary>
public sealed class WorkThreadViewState
{
    /// <summary>
    /// Gets or sets the ordered open thread identifiers.
    /// </summary>
    [JsonPropertyName("open_thread_ids")]
    public List<string> OpenThreadIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the persisted shell selection.
    /// </summary>
    [JsonPropertyName("selection")]
    public WorkThreadSelectionState Selection { get; set; } = WorkThreadSelectionState.GlobalDraft();

    /// <summary>
    /// Gets or sets the selected thread identifier.
    /// This legacy field remains for compatibility with older persisted state.
    /// </summary>
    [JsonPropertyName("selected_thread_id")]
    public string? SelectedThreadId { get; set; }

    /// <summary>
    /// Gets or sets the last update time.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets per-thread execution preferences restored by the terminal UI.
    /// </summary>
    [JsonPropertyName("thread_preferences")]
    public Dictionary<string, WorkThreadPreference> ThreadPreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets machine-local navigator settings.
    /// </summary>
    [JsonPropertyName("navigator")]
    public NavigatorSettings Navigator { get; set; } = new();

    /// <summary>
    /// Gets or sets machine-local thread metadata tracked outside backend-owned sessions.
    /// </summary>
    [JsonPropertyName("thread_states")]
    public Dictionary<string, WorkThreadLocalState> ThreadStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates the view state.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the data is invalid.</exception>
    public void Validate()
    {
        Selection ??= WorkThreadSelectionState.GlobalDraft();
        Selection.Validate();

        var duplicateThreadId = OpenThreadIds
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .Where(static x => x.Count() > 1)
            .Select(static x => x.Key)
            .FirstOrDefault();

        if (duplicateThreadId is not null)
        {
            throw new ArgumentException($"Open thread ids contain duplicate entry '{duplicateThreadId}'.", nameof(OpenThreadIds));
        }

        var selectedThreadId = Selection.Surface == WorkThreadSelectionSurface.Thread
            ? Selection.ThreadId
            : SelectedThreadId;
        if (!string.IsNullOrWhiteSpace(selectedThreadId)
            && !OpenThreadIds.Contains(selectedThreadId, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Selected thread id must be present in open_thread_ids.", nameof(Selection));
        }

        var legacySelectedThreadId = Selection.Surface == WorkThreadSelectionSurface.Thread
            ? Selection.ThreadId
            : null;
        if (!string.Equals(SelectedThreadId, legacySelectedThreadId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Selected thread id must mirror the persisted selection.", nameof(SelectedThreadId));
        }

        var invalidPreferenceKey = ThreadPreferences.Keys.FirstOrDefault(string.IsNullOrWhiteSpace);
        if (invalidPreferenceKey is not null)
        {
            throw new ArgumentException("Thread preference keys must be non-empty.", nameof(ThreadPreferences));
        }

        Navigator ??= new NavigatorSettings();
        Navigator.Validate();

        var invalidThreadStateKey = ThreadStates.Keys.FirstOrDefault(string.IsNullOrWhiteSpace);
        if (invalidThreadStateKey is not null)
        {
            throw new ArgumentException("Thread state keys must be non-empty.", nameof(ThreadStates));
        }

        foreach (var state in ThreadStates.Values)
        {
            state.Validate();
        }
    }
}

/// <summary>
/// Describes the persisted shell selection restored by the terminal UI.
/// </summary>
public sealed class WorkThreadSelectionState
{
    /// <summary>
    /// Gets or sets the selected shell surface.
    /// </summary>
    [JsonPropertyName("surface")]
    public WorkThreadSelectionSurface Surface { get; set; } = WorkThreadSelectionSurface.Draft;

    /// <summary>
    /// Gets or sets the draft scope when the draft workspace is selected.
    /// </summary>
    [JsonPropertyName("draft_scope")]
    public WorkThreadDraftScope DraftScope { get; set; } = WorkThreadDraftScope.Global;

    /// <summary>
    /// Gets or sets the selected or preferred project identifier.
    /// </summary>
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the selected thread identifier when a thread workspace is selected.
    /// </summary>
    [JsonPropertyName("thread_id")]
    public string? ThreadId { get; set; }

    /// <summary>
    /// Creates a persisted global draft selection.
    /// </summary>
    public static WorkThreadSelectionState GlobalDraft(string? projectId = null)
        => new()
        {
            Surface = WorkThreadSelectionSurface.Draft,
            DraftScope = WorkThreadDraftScope.Global,
            ProjectId = projectId,
        };

    /// <summary>
    /// Creates a persisted project draft selection.
    /// </summary>
    public static WorkThreadSelectionState ProjectDraft(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        return new WorkThreadSelectionState
        {
            Surface = WorkThreadSelectionSurface.Draft,
            DraftScope = WorkThreadDraftScope.Project,
            ProjectId = projectId,
        };
    }

    /// <summary>
    /// Creates a persisted thread selection.
    /// </summary>
    public static WorkThreadSelectionState Thread(string threadId, string? projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return new WorkThreadSelectionState
        {
            Surface = WorkThreadSelectionSurface.Thread,
            ProjectId = projectId,
            ThreadId = threadId,
        };
    }

    /// <summary>
    /// Validates the selection state.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the selection is invalid.</exception>
    public void Validate()
    {
        if (Surface == WorkThreadSelectionSurface.Thread)
        {
            if (string.IsNullOrWhiteSpace(ThreadId))
            {
                throw new ArgumentException("Thread selection must specify thread_id.", nameof(ThreadId));
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(ThreadId))
        {
            throw new ArgumentException("Draft selection cannot specify thread_id.", nameof(ThreadId));
        }

        if (DraftScope == WorkThreadDraftScope.Project && string.IsNullOrWhiteSpace(ProjectId))
        {
            throw new ArgumentException("Project draft selection must specify project_id.", nameof(ProjectId));
        }
    }
}

/// <summary>
/// Identifies the persisted shell surface.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkThreadSelectionSurface>))]
public enum WorkThreadSelectionSurface
{
    /// <summary>
    /// The draft workspace is selected.
    /// </summary>
    Draft,

    /// <summary>
    /// A thread workspace is selected.
    /// </summary>
    Thread,
}

/// <summary>
/// Identifies the persisted draft scope.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkThreadDraftScope>))]
public enum WorkThreadDraftScope
{
    /// <summary>
    /// The global draft scope is selected.
    /// </summary>
    Global,

    /// <summary>
    /// A project draft scope is selected.
    /// </summary>
    Project,
}

/// <summary>
/// Describes persisted machine-local metadata for a thread.
/// </summary>
public sealed class WorkThreadLocalState
{
    /// <summary>
    /// Gets or sets a value indicating whether the thread is archived locally.
    /// </summary>
    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    /// <summary>
    /// Gets or sets the cached message count when known.
    /// </summary>
    [JsonPropertyName("message_count")]
    public int? MessageCount { get; set; }

    /// <summary>
    /// Validates the thread local state.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the message count is negative.</exception>
    public void Validate()
    {
        if (MessageCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MessageCount), MessageCount, "MessageCount cannot be negative.");
        }
    }
}

/// <summary>
/// Describes a persisted model and reasoning override for a thread.
/// </summary>
public sealed class WorkThreadPreference
{
    /// <summary>
    /// Gets or sets the preferred model identifier.
    /// </summary>
    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    /// <summary>
    /// Gets or sets the preferred reasoning effort.
    /// </summary>
    [JsonPropertyName("reasoning_effort")]
    public AgentReasoningEffort? ReasoningEffort { get; set; }

}

