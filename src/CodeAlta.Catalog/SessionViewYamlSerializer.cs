using System.Text;
using System.Text.Json.Serialization;
using SharpYaml;

namespace CodeAlta.Catalog;

/// <summary>
/// Serializes and deserializes session-view metadata.
/// </summary>
public sealed class SessionViewYamlSerializer
{
    private sealed class SessionViewFrontMatter
    {
        [JsonPropertyName("session_id")]
        public string? SessionId { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        // Persisted front matter uses backend_id; the in-memory API exposes provider terminology.
        [JsonPropertyName("backend_id")]
        public string? ProviderId { get; set; }

        [JsonPropertyName("provider_key")]
        public string? ProviderKey { get; set; }

        [JsonPropertyName("project_ref")]
        public string? ProjectRef { get; set; }

        [JsonPropertyName("parent_session_id")]
        public string? ParentSessionId { get; set; }

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

    private sealed class SessionViewViewStateDocument
    {
        [JsonPropertyName("open_session_ids")]
        public List<string>? OpenSessionIds { get; set; }

        [JsonPropertyName("selection")]
        public SessionViewSelectionState? Selection { get; set; }

        [JsonPropertyName("selected_session_id")]
        public string? SelectedSessionId { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [JsonPropertyName("project_preferences")]
        public Dictionary<string, SessionViewPreference>? ProjectPreferences { get; set; }

        [JsonPropertyName("session_preferences")]
        public Dictionary<string, SessionViewPreference>? LegacySessionPreferences { get; set; }

        [JsonPropertyName("navigator")]
        public NavigatorSettings? Navigator { get; set; }

        [JsonPropertyName("session_states")]
        public Dictionary<string, SessionViewLocalState>? LegacySessionStates { get; set; }
    }

    /// <summary>
    /// Deserializes a session-view descriptor from markdown frontmatter.
    /// </summary>
    /// <param name="markdown">The markdown content.</param>
    /// <returns>The parsed descriptor.</returns>
    public SessionViewDescriptor DeserializeSessionMarkdown(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var document = ParseFrontMatter(markdown);
        var frontMatter = YamlSerializer.Deserialize<SessionViewFrontMatter>(document.FrontMatter) ?? new SessionViewFrontMatter();

        var sessionId = MigrateSessionId(frontMatter.SessionId, ExtractLegacySessionId(document.FrontMatter), frontMatter.ProviderId, frontMatter.ProviderKey);

        return new SessionViewDescriptor
        {
            SessionId = sessionId,
            Kind = ParseKind(frontMatter.Kind),
            ProviderId = frontMatter.ProviderId ?? string.Empty,
            ProviderKey = frontMatter.ProviderKey ?? frontMatter.ProviderId,
            ProjectRef = frontMatter.ProjectRef,
            ParentSessionId = frontMatter.ParentSessionId,
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
    /// Serializes a session-view descriptor to markdown frontmatter.
    /// </summary>
    /// <param name="descriptor">The descriptor to serialize.</param>
    /// <returns>Markdown text.</returns>
    public string SerializeSessionMarkdown(SessionViewDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var frontMatter = new SessionViewFrontMatter
        {
            SessionId = descriptor.SessionId,
            Kind = descriptor.Kind switch
            {
                SessionViewKind.GlobalSession => "global_session",
                SessionViewKind.ProjectSession => "project_session",
                SessionViewKind.InternalSession => "internal_session",
                _ => throw new InvalidOperationException($"Unsupported session kind '{descriptor.Kind}'."),
            },
            ProviderId = descriptor.ProviderId,
            ProviderKey = descriptor.ResolvedProviderKey,
            ProjectRef = descriptor.ProjectRef,
            ParentSessionId = descriptor.ParentSessionId,
            CreatedBy = descriptor.CreatedBy,
            WorkingDirectory = descriptor.WorkingDirectory,
            Title = descriptor.Title,
            Status = descriptor.Status switch
            {
                SessionViewStatus.Draft => "draft",
                SessionViewStatus.Active => "active",
                SessionViewStatus.Waiting => "waiting",
                SessionViewStatus.Blocked => "blocked",
                SessionViewStatus.Background => "background",
                SessionViewStatus.Completed => "completed",
                SessionViewStatus.Archived => "archived",
                _ => throw new InvalidOperationException($"Unsupported session status '{descriptor.Status}'."),
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
    /// Deserializes machine-local session-view view state from YAML.
    /// </summary>
    /// <param name="yaml">The YAML content.</param>
    /// <returns>The parsed view state.</returns>
    public SessionViewViewState DeserializeViewState(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var document = YamlSerializer.Deserialize<SessionViewViewStateDocument>(yaml) ?? new SessionViewViewStateDocument();
        var openSessionIds = document.OpenSessionIds ?? [];
        var selection = document.Selection ?? BuildLegacySelection(document.SelectedSessionId, openSessionIds);
        return new SessionViewViewState
        {
            OpenSessionIds = openSessionIds,
            Selection = selection,
            SelectedSessionId = selection.Surface == SessionViewSelectionSurface.Session ? selection.SessionId : null,
            UpdatedAt = document.UpdatedAt ?? default,
            ProjectPreferences = document.ProjectPreferences?.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, SessionViewPreference>(StringComparer.OrdinalIgnoreCase),
            SessionPreferences = document.LegacySessionPreferences?.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, SessionViewPreference>(StringComparer.OrdinalIgnoreCase),
            SessionStates = document.LegacySessionStates?.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, SessionViewLocalState>(StringComparer.OrdinalIgnoreCase),
            Navigator = document.Navigator ?? new NavigatorSettings(),
        };
    }

    /// <summary>
    /// Serializes machine-local session-view view state to YAML.
    /// </summary>
    /// <param name="viewState">The view state.</param>
    /// <returns>The serialized YAML.</returns>
    public string SerializeViewState(SessionViewViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        var document = new SessionViewViewStateDocument
        {
            OpenSessionIds = viewState.OpenSessionIds,
            Selection = viewState.Selection,
            SelectedSessionId = viewState.Selection.Surface == SessionViewSelectionSurface.Session
                ? viewState.Selection.SessionId
                : null,
            UpdatedAt = viewState.UpdatedAt,
            ProjectPreferences = viewState.ProjectPreferences,
            Navigator = viewState.Navigator,
        };

        return YamlSerializer.Serialize(document);
    }

    private static SessionViewSelectionState BuildLegacySelection(string? selectedSessionId, IReadOnlyList<string> openSessionIds)
    {
        if (!string.IsNullOrWhiteSpace(selectedSessionId))
        {
            return SessionViewSelectionState.Session(selectedSessionId, projectId: null);
        }

        if (openSessionIds.FirstOrDefault(static sessionId => !string.IsNullOrWhiteSpace(sessionId)) is { } firstOpenSessionId)
        {
            return SessionViewSelectionState.Session(firstOpenSessionId, projectId: null);
        }

        return SessionViewSelectionState.GlobalDraft();
    }

    private static SessionViewKind ParseKind(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "global_session" or "global" => SessionViewKind.GlobalSession,
            "project_session" => SessionViewKind.ProjectSession,
            "internal_session" => SessionViewKind.InternalSession,
            _ => SessionViewKind.ProjectSession,
        };
    }

    private static string MigrateSessionId(
        string? persistedSessionId,
        string? legacySessionId,
        string? ProviderId,
        string? providerKey)
    {
        var persistedId = NormalizeOptionalText(persistedSessionId);
        var legacyId = NormalizeOptionalText(legacySessionId);

        if (legacyId is not null)
        {
            if (persistedId is null)
            {
                return legacyId;
            }

            if (string.Equals(persistedId, legacyId, StringComparison.Ordinal))
            {
                return persistedId;
            }

            if (TryStripKnownProviderPrefix(persistedId, ProviderId, providerKey, out var strippedPersisted) &&
                string.Equals(strippedPersisted, legacyId, StringComparison.Ordinal))
            {
                return legacyId;
            }

            if (TryStripKnownProviderPrefix(legacyId, ProviderId, providerKey, out var stripped) &&
                string.Equals(stripped, persistedId, StringComparison.Ordinal))
            {
                return persistedId;
            }

            throw new InvalidDataException(
                $"Legacy session-view metadata has conflicting session ids '{persistedId}' and '{legacyId}'.");
        }

        if (persistedId is null)
        {
            return string.Empty;
        }

        return TryStripKnownProviderPrefix(persistedId, ProviderId, providerKey, out var migratedSessionId)
            ? migratedSessionId
            : persistedId;
    }

    private static bool TryStripKnownProviderPrefix(
        string sessionId,
        string? ProviderId,
        string? providerKey,
        out string stripped)
    {
        stripped = string.Empty;
        var separatorIndex = sessionId.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == sessionId.Length - 1)
        {
            return false;
        }

        var prefix = sessionId[..separatorIndex];
        if (!IsKnownProviderPrefix(prefix, ProviderId, providerKey))
        {
            return false;
        }

        stripped = sessionId[(separatorIndex + 1)..];
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

    private static SessionViewStatus ParseStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "draft" => SessionViewStatus.Draft,
            "active" => SessionViewStatus.Active,
            "waiting" => SessionViewStatus.Waiting,
            "blocked" => SessionViewStatus.Blocked,
            "background" => SessionViewStatus.Background,
            "completed" => SessionViewStatus.Completed,
            "archived" => SessionViewStatus.Archived,
            _ => SessionViewStatus.Draft,
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

