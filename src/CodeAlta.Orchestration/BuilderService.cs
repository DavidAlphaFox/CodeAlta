using CodeAlta.Persistence;

namespace CodeAlta.Orchestration;

/// <summary>
/// Applies builder verification updates to durable tasks and artifacts.
/// </summary>
public sealed class BuilderService
{
    private readonly TaskRepository _taskRepository;
    private readonly ArtifactStore _artifactStore;
    private readonly ArtifactRepository _artifactRepository;
    private readonly OrchestrationOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuilderService"/> class.
    /// </summary>
    /// <param name="taskRepository">Task repository.</param>
    /// <param name="artifactStore">Artifact store.</param>
    /// <param name="artifactRepository">Artifact repository.</param>
    /// <param name="options">Orchestration options.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    public BuilderService(
        TaskRepository taskRepository,
        ArtifactStore artifactStore,
        ArtifactRepository artifactRepository,
        OrchestrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        ArgumentNullException.ThrowIfNull(artifactStore);
        ArgumentNullException.ThrowIfNull(artifactRepository);
        ArgumentNullException.ThrowIfNull(options);

        _taskRepository = taskRepository;
        _artifactStore = artifactStore;
        _artifactRepository = artifactRepository;
        _options = options;
    }

    /// <summary>
    /// Marks a task complete and persists a verification artifact.
    /// </summary>
    /// <param name="request">Builder completion request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Builder execution result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when task does not exist.</exception>
    public async Task<BuilderExecutionResult> CompleteTaskAsync(
        BuilderExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.VerificationSummary))
        {
            throw new ArgumentException("Verification summary is required.", nameof(request));
        }

        var updated = await _taskRepository.UpdateAsync(
                new UpdateTaskRequest
                {
                    TaskId = request.TaskId,
                    Status = CodeAlta.Persistence.TaskStatus.Completed,
                },
            cancellationToken).ConfigureAwait(false);
        if (updated is null)
        {
            throw new InvalidOperationException($"Task '{request.TaskId}' was not found.");
        }

        await _taskRepository.AddNoteAsync(
            request.TaskId,
            request.VerificationSummary,
            cancellationToken).ConfigureAwait(false);

        var artifactId = ArtifactId.NewVersion7();
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(
            _options.ArtifactRoot,
            request.ProjectId ?? "global",
            "builder",
            $"{request.TaskId}.md");

        await _artifactStore.WriteMarkdownAsync(
            path,
            new ArtifactDocument
            {
                Frontmatter = new ArtifactFrontmatter
                {
                    Id = artifactId.ToString(),
                    Type = "builder.verification",
                    ProjectId = request.ProjectId,
                    Title = $"Verification for {request.TaskId}",
                    Tags = ["builder", "verification"],
                    Links = new ArtifactLinks
                    {
                        Tasks = [request.TaskId.ToString()],
                    },
                },
                Body = request.VerificationSummary.Trim(),
            },
            cancellationToken).ConfigureAwait(false);

        await _artifactRepository.UpsertAsync(
            new ArtifactRecord
            {
                ArtifactId = artifactId,
                Uri = $"artifact://builder/{request.TaskId}",
                ProjectId = request.ProjectId,
                Type = "builder.verification",
                Path = Path.GetFullPath(path),
                CreatedAt = now,
                UpdatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);
        await _artifactRepository.AddLinkAsync(
            new ArtifactLinkRecord
            {
                FromArtifactId = artifactId,
                ToKind = "task",
                ToId = request.TaskId.ToString(),
            },
            cancellationToken).ConfigureAwait(false);

        return new BuilderExecutionResult
        {
            TaskId = request.TaskId,
            VerificationArtifactId = artifactId,
        };
    }
}
