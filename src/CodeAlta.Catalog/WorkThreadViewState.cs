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
    /// Gets or sets the selected thread identifier.
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
    /// Validates the view state.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the data is invalid.</exception>
    public void Validate()
    {
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

        if (!string.IsNullOrWhiteSpace(SelectedThreadId)
            && !OpenThreadIds.Contains(SelectedThreadId, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Selected thread id must be present in open_thread_ids.", nameof(SelectedThreadId));
        }

        var invalidPreferenceKey = ThreadPreferences.Keys.FirstOrDefault(string.IsNullOrWhiteSpace);
        if (invalidPreferenceKey is not null)
        {
            throw new ArgumentException("Thread preference keys must be non-empty.", nameof(ThreadPreferences));
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

    /// <summary>
    /// Gets or sets a value indicating whether the thread should auto-scroll as new content arrives.
    /// </summary>
    [JsonPropertyName("auto_scroll")]
    public bool AutoScroll { get; set; } = true;
}

