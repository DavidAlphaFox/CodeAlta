using CodeAlta.Plugins.Abstractions;
using XenoAtom.Logging;

namespace CodeAlta.Plugins;

/// <summary>
/// Runtime-owned plugin task service that links scheduled work to the plugin activation lifetime.
/// </summary>
public sealed class PluginRuntimeTaskService : IPluginTaskService
{
    private readonly object _gate = new();
    private readonly CancellationToken _lifetimeCancellationToken;
    private readonly List<TrackedPluginTask> _runningTasks = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginRuntimeTaskService"/> class.
    /// </summary>
    /// <param name="lifetimeCancellationToken">The token cancelled when the plugin activation is deactivated.</param>
    public PluginRuntimeTaskService(CancellationToken lifetimeCancellationToken)
    {
        _lifetimeCancellationToken = lifetimeCancellationToken;
    }

    /// <inheritdoc />
    public bool HasRunningTasks
    {
        get
        {
            lock (_gate)
            {
                RemoveCompletedTasks();
                return _runningTasks.Count != 0;
            }
        }
    }

    /// <inheritdoc />
    public int RunningTaskCount
    {
        get
        {
            lock (_gate)
            {
                RemoveCompletedTasks();
                return _runningTasks.Count;
            }
        }
    }

    /// <inheritdoc />
    public PluginTaskHandle Run(string name, Func<CancellationToken, ValueTask> work, PluginTaskOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(work);

        options ??= new PluginTaskOptions();
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellationToken);
        var task = StartTask(work, cancellationTokenSource.Token, options.LongRunning);
        var handle = new PluginTaskHandle(
            name,
            options.Description,
            options.LongRunning,
            DateTimeOffset.UtcNow,
            task,
            () => RequestCancellation(cancellationTokenSource));
        var tracked = new TrackedPluginTask(handle, cancellationTokenSource);

        lock (_gate)
        {
            _runningTasks.Add(tracked);
        }

        _ = task.ContinueWith(
            _ => Complete(tracked),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return handle;
    }

    /// <inheritdoc />
    public async ValueTask WhenIdleAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task[] runningTasks;
            lock (_gate)
            {
                RemoveCompletedTasks();
                if (_runningTasks.Count == 0)
                {
                    return;
                }

                runningTasks = [.. _runningTasks.Select(static task => task.Handle.Completion)];
            }

            try
            {
                await Task.WhenAll(runningTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Completion failures are surfaced through PluginTaskHandle.Completion. Idle waits only gate unload.
            }
        }
    }

    /// <summary>
    /// Requests cancellation for all currently tracked plugin tasks.
    /// </summary>
    public void CancelAll()
    {
        TrackedPluginTask[] runningTasks;
        lock (_gate)
        {
            RemoveCompletedTasks();
            runningTasks = [.. _runningTasks];
        }

        foreach (var task in runningTasks)
        {
            RequestCancellation(task.CancellationTokenSource);
        }
    }

    private static Task StartTask(Func<CancellationToken, ValueTask> work, CancellationToken cancellationToken, bool longRunning)
    {
        return longRunning
            ? Task.Factory.StartNew(
                static state => RunWorkAsync((WorkState)state!),
                new WorkState(work, cancellationToken),
                cancellationToken,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default).Unwrap()
            : Task.Run(() => RunWorkAsync(new WorkState(work, cancellationToken)), cancellationToken);
    }

    private static async Task RunWorkAsync(WorkState state)
    {
        await state.Work(state.CancellationToken).ConfigureAwait(false);
    }

    private static void RequestCancellation(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The task has already completed and the tracker has released its cancellation source.
        }
    }

    private void Complete(TrackedPluginTask task)
    {
        lock (_gate)
        {
            _runningTasks.Remove(task);
        }

        task.CancellationTokenSource.Dispose();
    }

    private void RemoveCompletedTasks()
    {
        for (var index = _runningTasks.Count - 1; index >= 0; index--)
        {
            if (!_runningTasks[index].Handle.IsCompleted)
            {
                continue;
            }

            var task = _runningTasks[index];
            _runningTasks.RemoveAt(index);
            task.CancellationTokenSource.Dispose();
        }
    }

    private readonly record struct WorkState(Func<CancellationToken, ValueTask> Work, CancellationToken CancellationToken);

    private sealed record TrackedPluginTask(PluginTaskHandle Handle, CancellationTokenSource CancellationTokenSource);
}

internal sealed class PluginRuntimeServices : IPluginServices
{
    private const string UnresolvedProjectScopeId = "__codealta_unresolved_project_scope__";

    private readonly IPluginServices _inner;

    public PluginRuntimeServices(
        Logger logger,
        string pluginRuntimeKey,
        PluginScope scope,
        string? scopeProjectId,
        IPluginServices inner,
        IPluginTaskService tasks)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRuntimeKey);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tasks);
        Logger = logger;
        _inner = inner;
        Tasks = tasks;
        Alta = new PluginRuntimeAltaService(pluginRuntimeKey, scope, scopeProjectId, inner.Alta);
    }

    public Logger Logger { get; }

    public IPluginUiService Ui => _inner.Ui;

    public IPluginStateStore State => _inner.State;

    public IPluginWorkspaceService Workspace => _inner.Workspace;

    public IPluginSessionService Sessions => _inner.Sessions;

    public IPluginPromptService Prompts => _inner.Prompts;

    public IPluginAgentService Agents => _inner.Agents;

    public IPluginTaskService Tasks { get; }

    public IPluginAltaService Alta { get; }

    private sealed class PluginRuntimeAltaService(
        string pluginRuntimeKey,
        PluginScope scope,
        string? scopeProjectId,
        IPluginAltaService inner) : IPluginAltaService
    {
        public ValueTask<PluginAltaCommandResult> InvokeAsync(
            IReadOnlyList<string> args,
            string? stdin = null,
            PluginAltaInvocationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(args);
            var effectiveOptions = scope == PluginScope.Project
                ? (options ?? new PluginAltaInvocationOptions()) with
                {
                    SourceProjectId = string.IsNullOrWhiteSpace(scopeProjectId)
                        ? UnresolvedProjectScopeId
                        : scopeProjectId,
                }
                : options;
            return inner is IPluginAltaRuntimeService runtime
                ? runtime.InvokeAsync(pluginRuntimeKey, args, stdin, effectiveOptions, cancellationToken)
                : inner.InvokeAsync(args, stdin, effectiveOptions, cancellationToken);
        }
    }
}
