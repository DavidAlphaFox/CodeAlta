using CodeAlta.Persistence;

namespace CodeAlta.Orchestration.Context;

/// <summary>
/// Provides task and task event context for an active task id.
/// </summary>
public sealed class TaskContextProvider : IContextProvider
{
    private readonly TaskRepository _taskRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskContextProvider"/> class.
    /// </summary>
    /// <param name="taskRepository">Task repository.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskRepository"/> is <see langword="null"/>.</exception>
    public TaskContextProvider(TaskRepository taskRepository)
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        _taskRepository = taskRepository;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContextItem>> ProvideAsync(
        ContextProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TaskId is null)
        {
            return [];
        }

        var task = await _taskRepository.GetAsync(request.TaskId.Value, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return [];
        }

        var events = await _taskRepository.ListEventsAsync(task.TaskId, cancellationToken).ConfigureAwait(false);
        var eventSummary = string.Join(
            "\n",
            events.Take(8).Select(static x =>
                $"- {x.CreatedAt:O} {x.Kind} {(x.PayloadJson is null ? string.Empty : x.PayloadJson)}"));

        var content =
            $"""
            Task: {task.Title}
            Status: {task.Status}
            ProjectId: {task.ProjectId ?? "(none)"}
            Recent Events:
            {eventSummary}
            """;

        return
        [
            new ContextItem
            {
                Title = $"Task {task.TaskId}",
                Content = content.Trim(),
                SourceUri = $"task://{task.TaskId}",
                Priority = 10,
            },
        ];
    }
}
