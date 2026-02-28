using System.Text;
using CodeAlta.Persistence;

namespace CodeAlta.Orchestration;

/// <summary>
/// Creates durable plan artifacts and task trees.
/// </summary>
public sealed class PlannerService
{
    private readonly TaskRepository _taskRepository;
    private readonly ArtifactStore _artifactStore;
    private readonly ArtifactRepository _artifactRepository;
    private readonly OrchestrationOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlannerService"/> class.
    /// </summary>
    /// <param name="taskRepository">Task repository.</param>
    /// <param name="artifactStore">Artifact store.</param>
    /// <param name="artifactRepository">Artifact repository.</param>
    /// <param name="options">Orchestration options.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    public PlannerService(
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
    /// Creates a root task, child tasks, and a persisted plan artifact.
    /// </summary>
    /// <param name="request">Planning request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created plan result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <see cref="PlannerPlanRequest.Goal"/> is empty.</exception>
    public async Task<PlannerPlanResult> CreatePlanAsync(
        PlannerPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            throw new ArgumentException("Goal is required.", nameof(request));
        }

        var rootTask = await _taskRepository.CreateAsync(
            new CreateTaskRequest
            {
                Title = request.Goal.Trim(),
                WorkspaceId = request.WorkspaceId,
                ProjectId = request.ProjectId,
                AssignedAgentId = request.AssignedAgentId,
            },
            cancellationToken).ConfigureAwait(false);

        var childTaskIds = new List<TaskId>(request.Steps.Count);
        foreach (var step in request.Steps.Where(static x => !string.IsNullOrWhiteSpace(x)))
        {
            var child = await _taskRepository.CreateAsync(
                new CreateTaskRequest
                {
                    Title = step.Trim(),
                    WorkspaceId = request.WorkspaceId,
                    ProjectId = request.ProjectId,
                    ParentTaskId = rootTask.TaskId.ToString(),
                    AssignedAgentId = request.AssignedAgentId,
                },
                cancellationToken).ConfigureAwait(false);
            childTaskIds.Add(child.TaskId);
        }

        var planArtifactId = ArtifactId.NewVersion7();
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(
            _options.ArtifactRoot,
            request.WorkspaceId ?? "global",
            "plans",
            $"{rootTask.TaskId}.md");
        var body = BuildPlanMarkdown(rootTask, childTaskIds);

        await _artifactStore.WriteMarkdownAsync(
            path,
            new ArtifactDocument
            {
                Frontmatter = new ArtifactFrontmatter
                {
                    Id = planArtifactId.ToString(),
                    Type = "plan.output",
                    WorkspaceId = request.WorkspaceId,
                    ProjectId = request.ProjectId,
                    Title = $"Plan for {rootTask.Title}",
                    Tags = ["plan", "planner"],
                    Links = new ArtifactLinks
                    {
                        Tasks = [rootTask.TaskId.ToString()],
                    },
                },
                Body = body,
            },
            cancellationToken).ConfigureAwait(false);

        await _artifactRepository.UpsertAsync(
            new ArtifactRecord
            {
                ArtifactId = planArtifactId,
                Uri = $"artifact://plan/{rootTask.TaskId}",
                WorkspaceId = request.WorkspaceId,
                ProjectId = request.ProjectId,
                Type = "plan.output",
                Path = Path.GetFullPath(path),
                CreatedAt = now,
                UpdatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);
        await _artifactRepository.AddLinkAsync(
            new ArtifactLinkRecord
            {
                FromArtifactId = planArtifactId,
                ToKind = "task",
                ToId = rootTask.TaskId.ToString(),
            },
            cancellationToken).ConfigureAwait(false);

        return new PlannerPlanResult
        {
            RootTaskId = rootTask.TaskId,
            ChildTaskIds = childTaskIds,
            PlanArtifactId = planArtifactId,
        };
    }

    private static string BuildPlanMarkdown(TaskRecord rootTask, IReadOnlyList<TaskId> childTaskIds)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Plan: {rootTask.Title}");
        builder.AppendLine();
        builder.AppendLine($"Root Task: `{rootTask.TaskId}`");
        builder.AppendLine();
        builder.AppendLine("## Steps");
        builder.AppendLine();
        for (var i = 0; i < childTaskIds.Count; i++)
        {
            builder.AppendLine($"{i + 1}. `{childTaskIds[i]}`");
        }

        return builder.ToString();
    }
}
