using CodeAlta.Persistence;

namespace CodeAlta.Orchestration;

/// <summary>
/// Persists run summaries as compaction-safe knowledge artifacts.
/// </summary>
public sealed class KnowledgeService
{
    private readonly ArtifactStore _artifactStore;
    private readonly ArtifactRepository _artifactRepository;
    private readonly OrchestrationOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeService"/> class.
    /// </summary>
    /// <param name="artifactStore">Artifact store.</param>
    /// <param name="artifactRepository">Artifact repository.</param>
    /// <param name="options">Orchestration options.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    public KnowledgeService(
        ArtifactStore artifactStore,
        ArtifactRepository artifactRepository,
        OrchestrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(artifactStore);
        ArgumentNullException.ThrowIfNull(artifactRepository);
        ArgumentNullException.ThrowIfNull(options);

        _artifactStore = artifactStore;
        _artifactRepository = artifactRepository;
        _options = options;
    }

    /// <summary>
    /// Persists a run summary artifact.
    /// </summary>
    /// <param name="title">Summary title.</param>
    /// <param name="summary">Summary markdown content.</param>
    /// <param name="workspaceId">Optional workspace id.</param>
    /// <param name="projectId">Optional project id.</param>
    /// <param name="taskId">Optional related task id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created artifact id.</returns>
    public async Task<ArtifactId> WriteRunSummaryAsync(
        string title,
        string summary,
        string? workspaceId = null,
        string? projectId = null,
        TaskId? taskId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Summary is required.", nameof(summary));
        }

        var artifactId = ArtifactId.NewVersion7();
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(
            _options.ArtifactRoot,
            workspaceId ?? "global",
            "summaries",
            $"{artifactId}.md");

        await _artifactStore.WriteMarkdownAsync(
            path,
            new ArtifactDocument
            {
                Frontmatter = new ArtifactFrontmatter
                {
                    Id = artifactId.ToString(),
                    Type = "run.summary",
                    WorkspaceId = workspaceId,
                    ProjectId = projectId,
                    Title = title.Trim(),
                    Tags = ["summary"],
                    Links = new ArtifactLinks
                    {
                        Tasks = taskId.HasValue ? [taskId.Value.ToString()] : [],
                    },
                },
                Body = summary.Trim(),
            },
            cancellationToken).ConfigureAwait(false);

        await _artifactRepository.UpsertAsync(
            new ArtifactRecord
            {
                ArtifactId = artifactId,
                Uri = $"artifact://summary/{artifactId}",
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                Type = "run.summary",
                Path = Path.GetFullPath(path),
                CreatedAt = now,
                UpdatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        if (taskId.HasValue)
        {
            await _artifactRepository.AddLinkAsync(
                new ArtifactLinkRecord
                {
                    FromArtifactId = artifactId,
                    ToKind = "task",
                    ToId = taskId.Value.ToString(),
                },
                cancellationToken).ConfigureAwait(false);
        }

        return artifactId;
    }
}
