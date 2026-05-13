using CodeAlta.Threading;
using System.Collections.Concurrent;
using System.Threading;
using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Views;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal sealed class CodeAltaShellController : IThreadRuntimeEventProjector, IAsyncDisposable
{
    private readonly ICodeAltaShell _shell;
    private readonly IKnownProjectImporter _knownProjectImporter;
    private readonly IProjectCatalogStore _projectCatalog;
    private readonly IRecoverableThreadSource _recoverableThreadSource;
    private readonly IWorkThreadDeleter _threadDeleter;
    private readonly IReadOnlyList<AgentBackendDescriptor> _backendDescriptors;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentQueue<WorkThreadRuntimeEvent> _pendingRuntimeEvents = new();
    private IUiDispatcher? _uiDispatcher;
    private int _runtimeEventDrainScheduled;
    private long _providerSessionLoadStatusVersion;
    private CancellationTokenSource? _initializationCts;
    private Task? _initializationTask;

    public CodeAltaShellController(
        ICodeAltaShell shell,
        IKnownProjectImporter knownProjectImporter,
        IProjectCatalogStore projectCatalog,
        IRecoverableThreadSource recoverableThreadSource,
        IWorkThreadDeleter threadDeleter,
        IReadOnlyList<AgentBackendDescriptor>? backendDescriptors = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(knownProjectImporter);
        ArgumentNullException.ThrowIfNull(projectCatalog);
        ArgumentNullException.ThrowIfNull(recoverableThreadSource);
        ArgumentNullException.ThrowIfNull(threadDeleter);

        _shell = shell;
        _knownProjectImporter = knownProjectImporter;
        _projectCatalog = projectCatalog;
        _recoverableThreadSource = recoverableThreadSource;
        _threadDeleter = threadDeleter;
        _backendDescriptors = backendDescriptors ?? [];
    }

    public void AttachUiDispatcher(IUiDispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        _uiDispatcher = uiDispatcher;
        if (!_pendingRuntimeEvents.IsEmpty)
        {
            ScheduleRuntimeEventDrain();
        }
    }

    public void StartInitialization(CancellationToken cancellationToken)
    {
        if (_initializationTask is not null)
        {
            return;
        }

        _initializationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        // Shell initialization intentionally runs off the UI flow. Any continuation that touches
        // shell/view state must marshal back through UiDispatcher.
        _initializationTask = Task.Run(
            () => RunInitializationAsync(_initializationCts.Token),
            CancellationToken.None);
        global::CodeAlta.CodeAltaTaskMonitor.Observe(_initializationTask, "Shell initialization");
    }

    public async Task ReloadCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            await UiDispatcher.InvokeAsync(
                    () => _shell.SetStatus("Refreshing project and thread catalog...", showSpinner: true))
                .ConfigureAwait(false);

            await _knownProjectImporter.ImportAsync(cancellationToken).ConfigureAwait(false);
            var projects = await _projectCatalog.LoadAsync(cancellationToken).ConfigureAwait(false);
            await ApplyRecoverableThreadsProgressivelyAsync(projects, shouldListBackendSessions: null, cancellationToken).ConfigureAwait(false);

            await UiDispatcher.InvokeAsync(
                    () =>
                    {
                        _shell.SetReadyStatusForCurrentSelection();
                    })
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await UiDispatcher.InvokeAsync(
                    () => _shell.SetStatus($"Failed to refresh catalog: {ex.Message}", tone: StatusTone.Error))
                .ConfigureAwait(false);
        }
    }

    public Task ApplyRuntimeEventAsync(WorkThreadRuntimeEvent runtimeEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        cancellationToken.ThrowIfCancellationRequested();
        return UiDispatcher.InvokeAsync(() => _shell.HandleRuntimeEvent(runtimeEvent));
    }

    public void QueueRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        cancellationToken.ThrowIfCancellationRequested();
        _pendingRuntimeEvents.Enqueue(runtimeEvent);
        ScheduleRuntimeEventDrain();
    }

    public int DrainPendingRuntimeEvents(int maxEvents = 512)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEvents);

        var drainedEvents = 0;
        WorkThreadRuntimeEvent? pendingEvent = null;
        while (drainedEvents < maxEvents && _pendingRuntimeEvents.TryDequeue(out var runtimeEvent))
        {
            drainedEvents++;
            if (pendingEvent is null)
            {
                pendingEvent = runtimeEvent;
                continue;
            }

            if (TryMergeRuntimeEvents(pendingEvent, runtimeEvent, out var mergedEvent))
            {
                pendingEvent = mergedEvent;
                continue;
            }

            _shell.HandleRuntimeEvent(pendingEvent);
            pendingEvent = runtimeEvent;
        }

        if (pendingEvent is not null)
        {
            _shell.HandleRuntimeEvent(pendingEvent);
        }

        return drainedEvents;
    }

    public Task SelectGlobalScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UiDispatcher.InvokeAsync(_shell.SelectGlobalScope);
    }

    public Task SelectProjectScopeAsync(string projectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        cancellationToken.ThrowIfCancellationRequested();
        return UiDispatcher.InvokeAsync(() => _shell.SelectProjectScope(projectId));
    }

    public Task OpenThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        cancellationToken.ThrowIfCancellationRequested();
        return UiDispatcher.InvokeAsync(
            () =>
            {
                _shell.OpenThread(threadId);
                _shell.FocusPromptEditor();
            });
    }

    public Task<ProjectDescriptor> OpenFolderAsync(string folderPath, CancellationToken cancellationToken)
        => OpenFolderAsync(folderPath, includeHidden: false, cancellationToken);

    public async Task<ProjectDescriptor> OpenFolderAsync(string folderPath, bool includeHidden, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        cancellationToken.ThrowIfCancellationRequested();

        await UiDispatcher.InvokeAsync(
                () => _shell.SetStatus($"Opening '{folderPath}'...", showSpinner: true))
            .ConfigureAwait(false);

        var project = await ResolveOpenProjectAsync(folderPath, includeHidden, cancellationToken).ConfigureAwait(false);

        await UiDispatcher.InvokeAsync(
                () =>
                {
                    _shell.UpsertProject(project);
                    _shell.SelectProjectScope(project.Id);
                    _shell.SetReadyStatusForCurrentSelection();
                    _shell.FocusPromptEditor();
                })
            .ConfigureAwait(false);

        return project;
    }

    public async Task<IReadOnlyList<WorkThreadDescriptor>> LoadProjectThreadsAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var threads = await _recoverableThreadSource.ListRecoverableThreadsAsync(cancellationToken).ConfigureAwait(false);
        return threads
            .Where(thread =>
                string.Equals(thread.ProjectRef, projectId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static thread => thread.LastActiveAt)
            .ThenBy(static thread => thread.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static thread => thread.ThreadId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<ProjectDescriptor?> GetProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        return _projectCatalog.GetByIdAsync(projectId, cancellationToken);
    }

    public async Task SaveProjectAsync(ProjectDescriptor project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);

        await _projectCatalog.SaveAsync(project, cancellationToken).ConfigureAwait(false);
        await ReloadCatalogAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeleteThreadResult> DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var threads = await _recoverableThreadSource.ListRecoverableThreadsAsync(cancellationToken).ConfigureAwait(false);
        var thread = threads.FirstOrDefault(candidate => string.Equals(candidate.ThreadId, threadId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Thread '{threadId}' was not found.");

        var threadsToDelete = CollectThreadSubtree(thread, threads);
        var deletedByBackend = false;
        foreach (var candidate in threadsToDelete)
        {
            deletedByBackend |= await _threadDeleter.DeleteThreadAsync(candidate, cancellationToken).ConfigureAwait(false);
        }

        await ReloadCatalogAsync(cancellationToken).ConfigureAwait(false);
        return new DeleteThreadResult(
            threadsToDelete.Select(static candidate => candidate.ThreadId).ToArray(),
            deletedByBackend);
    }

    public async Task<DeleteProjectResult> DeleteProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var project = await _projectCatalog.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Project '{projectId}' was not found.");
        var threads = await LoadProjectThreadsAsync(projectId, cancellationToken).ConfigureAwait(false);

        foreach (var thread in threads)
        {
            await _threadDeleter.DeleteThreadAsync(thread, cancellationToken).ConfigureAwait(false);
        }

        project.Archived = true;
        await _projectCatalog.SaveAsync(project, cancellationToken).ConfigureAwait(false);
        await ReloadCatalogAsync(cancellationToken).ConfigureAwait(false);
        return new DeleteProjectResult(project.Id, threads.Select(static thread => thread.ThreadId).ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _initializationCts?.Cancel();

        if (_initializationTask is not null)
        {
            try
            {
                await _initializationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _initializationCts?.Dispose();
        _disposeCts.Dispose();
    }

    private IUiDispatcher UiDispatcher
        => _uiDispatcher ?? throw new InvalidOperationException("The UI dispatcher must be attached before shell operations begin.");

    private static IReadOnlyList<WorkThreadDescriptor> CollectThreadSubtree(
        WorkThreadDescriptor root,
        IReadOnlyList<WorkThreadDescriptor> threads)
    {
        var byParentId = threads
            .Where(static thread => !string.IsNullOrWhiteSpace(thread.ParentThreadId))
            .GroupBy(static thread => thread.ParentThreadId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WorkThreadDescriptor>();
        Collect(root);
        result.Reverse();
        return result;

        void Collect(WorkThreadDescriptor thread)
        {
            if (!visited.Add(thread.ThreadId))
            {
                return;
            }

            result.Add(thread);
            if (!byParentId.TryGetValue(thread.ThreadId, out var children))
            {
                return;
            }

            foreach (var child in children)
            {
                Collect(child);
            }
        }
    }

    private void ScheduleRuntimeEventDrain()
    {
        var uiDispatcher = _uiDispatcher;
        if (uiDispatcher is null || Interlocked.Exchange(ref _runtimeEventDrainScheduled, 1) != 0)
        {
            return;
        }

        UiDispatch.Post(uiDispatcher, DrainPendingRuntimeEventsOnUi);
    }

    private void DrainPendingRuntimeEventsOnUi()
    {
        _ = DrainPendingRuntimeEvents();
        Interlocked.Exchange(ref _runtimeEventDrainScheduled, 0);
        if (!_pendingRuntimeEvents.IsEmpty)
        {
            ScheduleRuntimeEventDrain();
        }
    }

    internal Task InitializeAsync(CancellationToken cancellationToken)
        => RunInitializationAsync(cancellationToken);

    private async Task<ProjectDescriptor> ResolveOpenProjectAsync(
        string folderPathOrProjectReference,
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPathOrProjectReference);

        if (OpenProjectRequestResolver.LooksLikePath(folderPathOrProjectReference))
        {
            var normalizedPath = OpenProjectRequestResolver.NormalizePath(folderPathOrProjectReference);
            if (!Directory.Exists(normalizedPath))
            {
                throw new InvalidOperationException($"The folder '{normalizedPath}' does not exist.");
            }

            return await _projectCatalog.UpsertFromPathAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        }

        var projects = await _projectCatalog.LoadAsync(cancellationToken).ConfigureAwait(false);
        var candidateProjects = includeHidden
            ? projects
            : projects.Where(static project => !project.Archived).ToArray();
        var project = OpenProjectRequestResolver.ResolveProjectReference(candidateProjects, folderPathOrProjectReference);
        if (!project.Archived)
        {
            return project;
        }

        project.Archived = false;
        await _projectCatalog.SaveAsync(project, cancellationToken).ConfigureAwait(false);
        return project;
    }

    private async Task RunInitializationAsync(CancellationToken cancellationToken)
    {
        var initializedForInteraction = false;
        try
        {
            // These startup calls are background I/O and must not assume UI-thread affinity.
            var startupProviderLoadTask = Task.Run(
                () => InitializeAndLoadStartupProviderStateAsync(cancellationToken),
                CancellationToken.None);
            await MarkInitializedForInteractionAsync(cancellationToken).ConfigureAwait(false);
            initializedForInteraction = true;
            await startupProviderLoadTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (!_disposeCts.IsCancellationRequested)
            {
                if (!initializedForInteraction)
                {
                    await MarkInitializedForInteractionAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task InitializeAndLoadStartupProviderStateAsync(CancellationToken cancellationToken)
    {
        if (_backendDescriptors.Count == 0 || _knownProjectImporter is not IKnownProjectImporterWithProgress progressImporter)
        {
            await _shell.InitializeChatBackendsAsync(cancellationToken).ConfigureAwait(false);
            await RefreshCatalogFromBackendsAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var progress = new ProviderStartupLoadProgress(_backendDescriptors);
            var recoveredThreads = new Dictionary<string, WorkThreadDescriptor>(StringComparer.OrdinalIgnoreCase);
            using var applyGate = new SemaphoreSlim(initialCount: 1, maxCount: 1);

            ReportStartupProviderLoadProgress(progress.Snapshot(null));
            var providerTasks = _backendDescriptors
                .Select(descriptor => InitializeLoadAndRecoverProviderAsync(descriptor, progressImporter, progress, recoveredThreads, applyGate, cancellationToken))
                .ToArray();
            await Task.WhenAll(providerTasks).ConfigureAwait(false);

            var projects = await _projectCatalog.LoadAsync(cancellationToken).ConfigureAwait(false);
            var threads = recoveredThreads.Values
                .OrderByDescending(static thread => thread.LastActiveAt)
                .ToArray();

            await UiDispatcher.InvokeAsync(
                    () =>
                    {
                        _shell.ApplyRecoveredCatalogState(projects, threads);
                        _shell.TrySchedulePendingStartupThreadRestore(CancellationToken.None);
                    })
                .ConfigureAwait(false);
            await SetProviderSessionLoadStatusAsync(null)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await SetProviderSessionLoadStatusAsync(null).ConfigureAwait(false);
            if (LogManager.IsInitialized && CodeAltaApp.UiLogger.IsEnabled(LogLevel.Error))
            {
                CodeAltaApp.UiLogger.Error(ex, "Failed to refresh backend startup state.");
            }
        }
    }

    private async Task InitializeLoadAndRecoverProviderAsync(
        AgentBackendDescriptor descriptor,
        IKnownProjectImporterWithProgress projectImporter,
        ProviderStartupLoadProgress progress,
        Dictionary<string, WorkThreadDescriptor> recoveredThreads,
        SemaphoreSlim applyGate,
        CancellationToken cancellationToken)
    {
        Task? backendInitializationTask = null;
        try
        {
            backendInitializationTask = _shell.InitializeChatBackendAsync(descriptor.BackendId, cancellationToken);
            await projectImporter.ImportBackendAsync(descriptor, cancellationToken).ConfigureAwait(false);
            await ApplyBackendRecoverableThreadsAsync(descriptor.BackendId, recoveredThreads, applyGate, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (backendInitializationTask is not null)
                {
                    await backendInitializationTask.ConfigureAwait(false);
                }
            }
            finally
            {
                ReportStartupProviderLoadProgress(progress.Snapshot(descriptor));
            }
        }
    }

    private Task MarkInitializedForInteractionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UiDispatcher.InvokeAsync(
            () =>
            {
                _shell.PublishStartupCatalogProjectionReady();
                _shell.SetReadyStatusForCurrentSelection();
                _shell.SetInitialized(true);
                _shell.TrySchedulePendingStartupThreadRestore(CancellationToken.None);
            });
    }

    private async Task RefreshCatalogFromBackendsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var progressiveRecovery = new ProgressiveRecoveryCoordinator(this, cancellationToken);
            if (_knownProjectImporter is IKnownProjectImporterWithProgress progressImporter)
            {
                await progressImporter.ImportAsync(
                        progress =>
                        {
                            ReportProviderSessionLoadProgress(progress);
                            progressiveRecovery.TryStartBackendRecovery(progress.BackendId);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _knownProjectImporter.ImportAsync(cancellationToken).ConfigureAwait(false);
                progressiveRecovery.StartAllBackendRecovery();
            }

            await progressiveRecovery.WhenStartedRecoveriesCompleteAsync().ConfigureAwait(false);

            var projects = await _projectCatalog.LoadAsync(cancellationToken).ConfigureAwait(false);
            await ApplyRecoverableThreadsProgressivelyAsync(projects, shouldListBackendSessions: null, cancellationToken).ConfigureAwait(false);
            await SetProviderSessionLoadStatusAsync(null)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await SetProviderSessionLoadStatusAsync(null).ConfigureAwait(false);
            if (LogManager.IsInitialized && CodeAltaApp.UiLogger.IsEnabled(LogLevel.Error))
            {
                CodeAltaApp.UiLogger.Error(ex, "Failed to refresh backend startup state.");
            }
        }
    }

    private async Task ApplyBackendRecoverableThreadsAsync(
        AgentBackendId backendId,
        Dictionary<string, WorkThreadDescriptor> recoveredThreads,
        SemaphoreSlim applyGate,
        CancellationToken cancellationToken)
    {
        var projects = await _projectCatalog.LoadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var thread in _recoverableThreadSource
                               .StreamRecoverableThreadsAsync(candidate => candidate == backendId, cancellationToken)
                               .ConfigureAwait(false))
            {
                await ApplyRecoveredThreadSnapshotAsync(
                        projects,
                        recoveredThreads,
                        thread,
                        applyGate,
                        pruneMissingThreads: false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (LogManager.IsInitialized && CodeAltaApp.UiLogger.IsEnabled(LogLevel.Debug))
            {
                CodeAltaApp.UiLogger.Debug(ex, $"Failed to progressively recover sessions for backend '{backendId.Value}'.");
            }

            return;
        }
    }

    private async Task ApplyRecoverableThreadsProgressivelyAsync(
        IReadOnlyList<ProjectDescriptor> projects,
        Func<AgentBackendId, bool>? shouldListBackendSessions,
        CancellationToken cancellationToken)
    {
        var recoveredThreads = new Dictionary<string, WorkThreadDescriptor>(StringComparer.OrdinalIgnoreCase);
        using var applyGate = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        var appliedAny = false;
        var threads = shouldListBackendSessions is null
            ? _recoverableThreadSource.StreamRecoverableThreadsAsync(cancellationToken)
            : _recoverableThreadSource.StreamRecoverableThreadsAsync(shouldListBackendSessions, cancellationToken);
        await foreach (var thread in threads.ConfigureAwait(false))
        {
            appliedAny = true;
            await ApplyRecoveredThreadSnapshotAsync(
                    projects,
                    recoveredThreads,
                    thread,
                    applyGate,
                    pruneMissingThreads: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!appliedAny)
        {
            await UiDispatcher.InvokeAsync(
                    () =>
                    {
                        _shell.ApplyRecoveredCatalogState(projects, []);
                        _shell.TrySchedulePendingStartupThreadRestore(CancellationToken.None);
                    })
                .ConfigureAwait(false);
            return;
        }

        await ApplyRecoveredThreadSnapshotAsync(
                projects,
                recoveredThreads,
                thread: null,
                applyGate,
                pruneMissingThreads: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyRecoveredThreadSnapshotAsync(
        IReadOnlyList<ProjectDescriptor> projects,
        Dictionary<string, WorkThreadDescriptor> recoveredThreads,
        WorkThreadDescriptor? thread,
        SemaphoreSlim applyGate,
        bool pruneMissingThreads,
        CancellationToken cancellationToken)
    {
        await applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (thread is not null)
            {
                recoveredThreads[thread.ThreadId] = thread;
            }

            var threads = recoveredThreads.Values
                .OrderByDescending(static item => item.LastActiveAt)
                .ToArray();

            await UiDispatcher.InvokeAsync(
                    () =>
                    {
                        _shell.ApplyRecoveredCatalogState(projects, threads, pruneMissingThreads);
                        _shell.TrySchedulePendingStartupThreadRestore(CancellationToken.None);
                    })
                .ConfigureAwait(false);
        }
        finally
        {
            applyGate.Release();
        }
    }

    private sealed class ProgressiveRecoveryCoordinator
    {
        private readonly CodeAltaShellController _owner;
        private readonly CancellationToken _cancellationToken;
        private readonly object _gate = new();
        private readonly Dictionary<string, WorkThreadDescriptor> _recoveredThreads = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _applyGate = new(initialCount: 1, maxCount: 1);
        private readonly HashSet<AgentBackendId> _startedBackendIds = [];
        private readonly List<Task> _tasks = [];

        public ProgressiveRecoveryCoordinator(CodeAltaShellController owner, CancellationToken cancellationToken)
        {
            _owner = owner;
            _cancellationToken = cancellationToken;
        }

        public void StartAllBackendRecovery()
        {
            foreach (var descriptor in _owner._backendDescriptors)
            {
                TryStartBackendRecovery(descriptor.BackendId);
            }
        }

        public void TryStartBackendRecovery(AgentBackendId backendId)
        {
            if (string.IsNullOrWhiteSpace(backendId.Value))
            {
                return;
            }

            Task task;
            lock (_gate)
            {
                if (!_startedBackendIds.Add(backendId))
                {
                    return;
                }

                task = _owner.ApplyBackendRecoverableThreadsAsync(
                    backendId,
                    _recoveredThreads,
                    _applyGate,
                    _cancellationToken);
                _tasks.Add(task);
            }
        }

        public Task WhenStartedRecoveriesCompleteAsync()
        {
            Task[] tasks;
            lock (_gate)
            {
                tasks = _tasks.ToArray();
            }

            return tasks.Length == 0 ? Task.CompletedTask : Task.WhenAll(tasks);
        }
    }

    private void ReportProviderSessionLoadProgress(ProviderSessionLoadProgress progress)
    {
        var status = FormatProviderSessionLoadStatus(progress);
        QueueProviderSessionLoadStatus(status);
    }

    private void ReportStartupProviderLoadProgress(ProviderSessionLoadProgress progress)
    {
        var status = FormatStartupProviderLoadStatus(progress);
        QueueProviderSessionLoadStatus(status);
    }

    private void QueueProviderSessionLoadStatus(string? status)
    {
        var version = Interlocked.Increment(ref _providerSessionLoadStatusVersion);
        _ = UiDispatcher.InvokeAsync(
            () =>
            {
                if (version == Volatile.Read(ref _providerSessionLoadStatusVersion))
                {
                    _shell.SetProviderSessionLoadStatus(status);
                }
            });
    }

    private Task SetProviderSessionLoadStatusAsync(string? status)
    {
        var version = Interlocked.Increment(ref _providerSessionLoadStatusVersion);
        return UiDispatcher.InvokeAsync(
            () =>
            {
                if (version == Volatile.Read(ref _providerSessionLoadStatusVersion))
                {
                    _shell.SetProviderSessionLoadStatus(status);
                }
            });
    }

    internal static string? FormatStartupProviderLoadStatus(ProviderSessionLoadProgress progress)
    {
        if (progress.TotalProviderCount <= 0 ||
            progress.CompletedProviderCount >= progress.TotalProviderCount)
        {
            return null;
        }

        var completed = Math.Clamp(progress.CompletedProviderCount, 0, progress.TotalProviderCount);
        var loadingNames = progress.LoadingProviderDisplayNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        var loadingText = loadingNames.Length == 0
            ? "providers"
            : string.Join(", ", loadingNames) + (progress.LoadingProviderDisplayNames.Count > loadingNames.Length ? ", …" : string.Empty);

        return $"Loading {loadingText} {BuildProgressBar(completed, progress.TotalProviderCount)} {completed}/{progress.TotalProviderCount}";
    }

    internal static string? FormatProviderSessionLoadStatus(ProviderSessionLoadProgress progress)
    {
        if (progress.TotalProviderCount <= 0 ||
            progress.CompletedProviderCount >= progress.TotalProviderCount)
        {
            return null;
        }

        var completed = Math.Clamp(progress.CompletedProviderCount, 0, progress.TotalProviderCount);
        var loadingNames = progress.LoadingProviderDisplayNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        var loadingText = loadingNames.Length == 0
            ? "provider sessions"
            : string.Join(", ", loadingNames) + (progress.LoadingProviderDisplayNames.Count > loadingNames.Length ? ", …" : string.Empty) + " sessions";

        return $"Loading {loadingText} {BuildProgressBar(completed, progress.TotalProviderCount)} {completed}/{progress.TotalProviderCount}";
    }

    private static string BuildProgressBar(int completed, int total)
    {
        const int width = 8;
        var filled = total <= 0 ? 0 : (int)Math.Round((double)completed / total * width, MidpointRounding.AwayFromZero);
        filled = Math.Clamp(filled, 0, width);
        return "[" + new string('■', filled) + new string('□', width - filled) + "]";
    }

    private sealed class ProviderStartupLoadProgress
    {
        private readonly object _gate = new();
        private readonly List<string> _loadingProviderDisplayNames;
        private int _completedProviderCount;

        public ProviderStartupLoadProgress(IReadOnlyList<AgentBackendDescriptor> descriptors)
        {
            _loadingProviderDisplayNames = descriptors.Select(static descriptor => descriptor.DisplayName).ToList();
            TotalProviderCount = descriptors.Count;
        }

        public int TotalProviderCount { get; }

        public ProviderSessionLoadProgress Snapshot(AgentBackendDescriptor? completedDescriptor)
        {
            lock (_gate)
            {
                if (completedDescriptor is not null)
                {
                    _completedProviderCount++;
                    _loadingProviderDisplayNames.RemoveAll(
                        name => string.Equals(name, completedDescriptor.DisplayName, StringComparison.Ordinal));
                }

                return new ProviderSessionLoadProgress(
                    completedDescriptor?.BackendId ?? default,
                    completedDescriptor?.DisplayName ?? string.Empty,
                    _completedProviderCount,
                    TotalProviderCount,
                    _loadingProviderDisplayNames.ToArray());
            }
        }
    }

    internal static bool TryMergeRuntimeEvents(
        WorkThreadRuntimeEvent first,
        WorkThreadRuntimeEvent second,
        out WorkThreadRuntimeEvent? merged)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first is WorkThreadAgentEvent { Event: AgentContentDeltaEvent firstDelta } firstAgent &&
            second is WorkThreadAgentEvent { Event: AgentContentDeltaEvent secondDelta } secondAgent &&
            string.Equals(firstAgent.ThreadId, secondAgent.ThreadId, StringComparison.Ordinal) &&
            firstDelta.Kind == secondDelta.Kind &&
            string.Equals(firstDelta.ContentId, secondDelta.ContentId, StringComparison.Ordinal) &&
            string.Equals(firstDelta.ParentActivityId, secondDelta.ParentActivityId, StringComparison.Ordinal) &&
            string.Equals(firstDelta.SessionId, secondDelta.SessionId, StringComparison.Ordinal) &&
            firstDelta.BackendId == secondDelta.BackendId &&
            firstDelta.RunId == secondDelta.RunId)
        {
            merged = new WorkThreadAgentEvent(
                firstAgent.ThreadId,
                new AgentContentDeltaEvent(
                    firstDelta.BackendId,
                    firstDelta.SessionId,
                    secondDelta.Timestamp,
                    firstDelta.RunId,
                    firstDelta.Kind,
                    firstDelta.ContentId,
                    firstDelta.ParentActivityId,
                    string.Concat(firstDelta.Delta, secondDelta.Delta)));
            return true;
        }

        merged = null;
        return false;
    }
}
