using System.Text;
using System.Text.Json.Serialization;
using SharpYaml;

namespace CodeAlta.Catalog;

/// <summary>
/// Serializes and deserializes work-thread metadata.
/// </summary>
public sealed class WorkThreadYamlSerializer
{
    private sealed class WorkThreadFrontMatter
    {
        [JsonPropertyName("thread_id")]
        public string? ThreadId { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        // Persisted front matter uses backend_id; the in-memory API exposes provider terminology.
        [JsonPropertyName("backend_id")]
        public string? ProviderId { get; set; }

        [JsonPropertyName("provider_key")]
        public string? ProviderKey { get; set; }

        [JsonPropertyName("project_ref")]
        public string? ProjectRef { get; set; }

        [JsonPropertyName("parent_thread_id")]
        public string? ParentThreadId { get; set; }

        [JsonPropertyName("created_by")]
        public AltaActorProvenance? CreatedBy { get; set; }

        [JsonPropertyName("working_directory")]
        public string? WorkingDirectory { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [JsonPropertyName("last_active_at")]
        public DateTimeOffset? LastActiveAt { get; set; }

        [JsonPropertyName("started_at")]
        public DateTimeOffset? StartedAt { get; set; }

        [JsonPropertyName("latest_summary")]
        public string? LatestSummary { get; set; }

        [JsonPropertyName("message_count")]
        public int? MessageCount { get; set; }
    }

    private sealed class WorkThreadViewStateDocument
    {
        [JsonPropertyName("open_thread_ids")]
        public List<string>? OpenThreadIds { get; set; }

        [JsonPropertyName("selection")]
        public WorkThreadSelectionState? Selection { get; set; }

        [JsonPropertyName("selected_thread_id")]
        public string? SelectedThreadId { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [JsonPropertyName("project_preferences")]
        public Dictionary<string, WorkThreadPreference>? ProjectPreferences { get; set; }

        [JsonPropertyName("thread_preferences")]
        public Dictionary<string, WorkThreadPreference>? LegacyThreadPreferences { get; set; }

        [JsonPropertyName("navigator")]
        public NavigatorSettings? Navigator { get; set; }

        [JsonPropertyName("thread_states")]
        public Dictionary<string, WorkThreadLocalState>? LegacyThreadStates { get; set; }
    }

    /// <summary>
    /// Deserializes a work-thread descriptor from markdown frontmatter.
    /// </summary>
    /// <param name="markdown">The markdown content.</param>
    /// <returns>The parsed descriptor.</returns>
    public SessionViewDescriptor DeserializeThreadMarkdown(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var document = ParseFrontMatter(markdown);
        var frontMatter = YamlSerializer.Deserialize<WorkThreadFrontMatter>(document.FrontMatter) ?? new WorkThreadFrontMatter();

        var threadId = MigrateThreadId(frontMatter.ThreadId, ExtractLegacySessionId(document.FrontMatter), frontMatter.ProviderId, frontMatter.ProviderKey);

        return new SessionViewDescriptor
        {
            ThreadId = threadId,
            Kind = ParseKind(frontMatter.Kind),
            ProviderId = frontMatter.ProviderId ?? string.Empty,
            ProviderKey = frontMatter.ProviderKey ?? frontMatter.ProviderId,
            ProjectRef = frontMatter.ProjectRef,
            ParentThreadId = frontMatter.ParentThreadId,
            CreatedBy = frontMatter.CreatedBy,
            WorkingDirectory = frontMatter.WorkingDirectory ?? string.Empty,
            Title = frontMatter.Title ?? string.Empty,
            Status = ParseStatus(frontMatter.Status),
            CreatedAt = frontMatter.CreatedAt ?? default,
            UpdatedAt = frontMatter.UpdatedAt ?? default,
            LastActiveAt = frontMatter.LastActiveAt ?? default,
            StartedAt = frontMatter.StartedAt,
            LatestSummary = frontMatter.LatestSummary,
            MessageCount = frontMatter.MessageCount,
            MarkdownBody = document.Body,
        };
    }

    /// <summary>
    /// Serializes a work-thread descriptor to markdown frontmatter.
    /// </summary>
    /// <param name="descriptor">The descriptor to serialize.</param>
    /// <returns>Markdown text.</returns>
    public string SerializeThreadMarkdown(SessionViewDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var frontMatter = new WorkThreadFrontMatter
        {
            ThreadId = descriptor.ThreadId,
            Kind = descriptor.Kind switch
            {
                WorkThreadKind.GlobalThread => "global_thread",
                WorkThreadKind.ProjectThread => "project_thread",
                WorkThreadKind.InternalThread => "internal_thread",
                _ => throw new InvalidOperationException($"Unsupported thread kind '{descriptor.Kind}'."),
            },
            ProviderId = descriptor.ProviderId,
            ProviderKey = descriptor.ResolvedProviderKey,
            ProjectRef = descriptor.ProjectRef,
            ParentThreadId = descriptor.ParentThreadId,
            CreatedBy = descriptor.CreatedBy,
            WorkingDirectory = descriptor.WorkingDirectory,
            Title = descriptor.Title,
            Status = descriptor.Status switch
            {
                WorkThreadStatus.Draft => "draft",
                WorkThreadStatus.Active => "active",
                WorkThreadStatus.Waiting => "waiting",
                WorkThreadStatus.Blocked => "blocked",
                WorkThreadStatus.Background => "background",
                WorkThreadStatus.Completed => "completed",
                WorkThreadStatus.Archived => "archived",
                _ => throw new InvalidOperationException($"Unsupported thread status '{descriptor.Status}'."),
            },
            CreatedAt = descriptor.CreatedAt,
            UpdatedAt = descriptor.UpdatedAt,
            LastActiveAt = descriptor.LastActiveAt,
            StartedAt = descriptor.StartedAt,
            LatestSummary = descriptor.LatestSummary,
            MessageCount = descriptor.MessageCount,
        };

        return SerializeMarkdown(frontMatter, descriptor.MarkdownBody, descriptor.Title);
    }

    /// <summary>
    /// Deserializes machine-local work-thread view state from YAML.
    /// </summary>
    /// <param name="yaml">The YAML content.</param>
    /// <returns>The parsed view state.</returns>
    public WorkThreadViewState DeserializeViewState(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var document = YamlSerializer.Deserialize<WorkThreadViewStateDocument>(yaml) ?? new WorkThreadViewStateDocument();
        var openThreadIds = document.OpenThreadIds ?? [];
        var selection = document.Selection ?? BuildLegacySelection(document.SelectedThreadId, openThreadIds);
        return new WorkThreadViewState
        {
            OpenThreadIds = openThreadIds,
            Selection = selection,
            SelectedThreadId = selection.Surface == WorkThreadSelectionSurface.Thread ? selection.ThreadId : null,
            UpdatedAt = document.UpdatedAt ?? default,
            ProjectPreferences = document.ProjectPreferences?.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, WorkThreadPreference>(StringComparer.OrdinalIgnoreCase),
            ThreadPreferences = document.LegacyThreadPreferences?.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, WorkThreadPreference>(StringComparer.OrdinalIgnoreCase),
            ThreadStates = document.LegacyThreadStates?.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, WorkThreadLocalState>(StringComparer.OrdinalIgnoreCase),
            Navigator = document.Navigator ?? new NavigatorSettings(),
        };
    }

    /// <summary>
    /// Serializes machine-local work-thread view state to YAML.
    /// </summary>
    /// <param name="viewState">The view state.</param>
    /// <returns>The serialized YAML.</returns>
    public string SerializeViewState(WorkThreadViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        var document = new WorkThreadViewStateDocument
        {
            OpenThreadIds = viewState.OpenThreadIds,
            Selection = viewState.Selection,
            SelectedThreadId = viewState.Selection.Surface == WorkThreadSelectionSurface.Thread
                ? viewState.Selection.ThreadId
                : null,
            UpdatedAt = viewState.UpdatedAt,
            ProjectPreferences = viewState.ProjectPreferences,
            Navigator = viewState.Navigator,
        };

        return YamlSerializer.Serialize(document);
    }

    private static WorkThreadSelectionState BuildLegacySelection(string? selectedThreadId, IReadOnlyList<string> openThreadIds)
    {
        if (!string.IsNullOrWhiteSpace(selectedThreadId))
        {
            return WorkThreadSelectionState.Thread(selectedThreadId, projectId: null);
        }

        if (openThreadIds.FirstOrDefault(static threadId => !string.IsNullOrWhiteSpace(threadId)) is { } firstOpenThreadId)
        {
            return WorkThreadSelectionState.Thread(firstOpenThreadId, projectId: null);
        }

        return WorkThreadSelectionState.GlobalDraft();
    }

    private static WorkThreadKind ParseKind(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "global_thread" or "global" => WorkThreadKind.GlobalThread,
            "project_thread" => WorkThreadKind.ProjectThread,
            "internal_thread" => WorkThreadKind.InternalThread,
            _ => WorkThreadKind.ProjectThread,
        };
    }

    private static string MigrateThreadId(
        string? persistedThreadId,
        string? legacySessionId,
        string? ProviderId,
        string? providerKey)
    {
        var threadId = NormalizeOptionalText(persistedThreadId);
        var sessionId = NormalizeOptionalText(legacySessionId);

        if (sessionId is not null)
        {
            if (threadId is null)
            {
                return sessionId;
            }

            if (string.Equals(threadId, sessionId, StringComparison.Ordinal))
            {
                return sessionId;
            }

            if (TryStripKnownProviderPrefix(threadId, ProviderId, providerKey, out var stripped) &&
                string.Equals(stripped, sessionId, StringComparison.Ordinal))
            {
                return sessionId;
            }

            throw new InvalidDataException(
                $"Legacy work-thread metadata has conflicting thread ids '{threadId}' and '{sessionId}'.");
        }

        if (threadId is null)
        {
            return string.Empty;
        }

        return TryStripKnownProviderPrefix(threadId, ProviderId, providerKey, out var migratedThreadId)
            ? migratedThreadId
            : threadId;
    }

    private static bool TryStripKnownProviderPrefix(
        string threadId,
        string? ProviderId,
        string? providerKey,
        out string stripped)
    {
        stripped = string.Empty;
        var separatorIndex = threadId.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == threadId.Length - 1)
        {
            return false;
        }

        var prefix = threadId[..separatorIndex];
        if (!IsKnownProviderPrefix(prefix, ProviderId, providerKey))
        {
            return false;
        }

        stripped = threadId[(separatorIndex + 1)..];
        return true;
    }

    private static bool IsKnownProviderPrefix(string prefix, string? ProviderId, string? providerKey)
        => string.Equals(prefix, ProviderId, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(prefix, providerKey, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ExtractLegacySessionId(string frontMatter)
    {
        var legacyKey = "backend_" + "session_id";
        using var reader = new StringReader(frontMatter.Replace("\r\n", "\n", StringComparison.Ordinal));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (!string.Equals(key, legacyKey, StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();
            return value.Trim('"', '\'');
        }

        return null;
    }

    private static WorkThreadStatus ParseStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "draft" => WorkThreadStatus.Draft,
            "active" => WorkThreadStatus.Active,
            "waiting" => WorkThreadStatus.Waiting,
            "blocked" => WorkThreadStatus.Blocked,
            "background" => WorkThreadStatus.Background,
            "completed" => WorkThreadStatus.Completed,
            "archived" => WorkThreadStatus.Archived,
            _ => WorkThreadStatus.Draft,
        };
    }

    private static (string FrontMatter, string Body) ParseFrontMatter(string markdown)
    {
        using var reader = new StringReader(markdown.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Markdown metadata must start with YAML frontmatter.");
        }

        var frontMatter = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                var body = reader.ReadToEnd();
                return (frontMatter.ToString(), body.TrimStart('\n'));
            }

            frontMatter.AppendLine(line);
        }

        throw new InvalidDataException("Markdown metadata frontmatter was not closed.");
    }

    private static string SerializeMarkdown<T>(T frontMatter, string? body, string title)
    {
        var yaml = YamlSerializer.Serialize(frontMatter).Trim();
        var markdownBody = string.IsNullOrWhiteSpace(body)
            ? $"# {title}"
            : body!.Trim();

        return $"---\n{yaml}\n---\n\n{markdownBody}\n";
    }
}

