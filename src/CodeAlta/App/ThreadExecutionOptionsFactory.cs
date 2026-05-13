using CodeAlta.Agent;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.LiveTool;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Threading;

namespace CodeAlta.App;

internal sealed class ThreadExecutionOptionsFactory
{
    private readonly CatalogOptions _catalogOptions;
    private readonly Dictionary<string, ChatBackendState> _chatBackendStates;
    private readonly ThreadSelectionContext _threadSelection;
    private readonly ThreadPermissionRequestCoordinator _permissionRequests;
    private readonly ThreadUserInputRequestCoordinator _userInputRequests;
    private readonly IServiceProvider? _altaServices;
    private readonly IReadOnlySet<string> _altaToolBackendIds;

    public ThreadExecutionOptionsFactory(
        CatalogOptions catalogOptions,
        Dictionary<string, ChatBackendState> chatBackendStates,
        ThreadSelectionContext threadSelection,
        ThreadPermissionRequestCoordinator permissionRequests,
        ThreadUserInputRequestCoordinator userInputRequests,
        IServiceProvider? altaServices = null,
        IReadOnlySet<string>? altaToolBackendIds = null)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(chatBackendStates);
        ArgumentNullException.ThrowIfNull(threadSelection);
        ArgumentNullException.ThrowIfNull(permissionRequests);
        ArgumentNullException.ThrowIfNull(userInputRequests);

        _catalogOptions = catalogOptions;
        _chatBackendStates = chatBackendStates;
        _threadSelection = threadSelection;
        _permissionRequests = permissionRequests;
        _userInputRequests = userInputRequests;
        _altaServices = altaServices;
        _altaToolBackendIds = altaToolBackendIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public WorkThreadExecutionOptions BuildPreferredExecutionOptions(
        AgentBackendId backendId,
        string workingDirectory,
        IReadOnlyList<string> projectRoots,
        Func<string?>? sourceThreadIdProvider = null)
    {
        ArgumentNullException.ThrowIfNull(projectRoots);

        _chatBackendStates.TryGetValue(backendId.Value, out var backendState);
        var model = backendState?.SelectedModelId;
        var reasoning = backendState?.SelectedReasoningEffort;

        var sourceProjectId = projectRoots.Count == 0
            ? null
            : _threadSelection.GetSelectedProjectId();
        return new WorkThreadExecutionOptions
        {
            BackendId = backendId,
            ProviderKey = backendId.Value,
            WorkingDirectory = workingDirectory,
            ProjectRoots = projectRoots,
            Model = model,
            ReasoningEffort = reasoning,
            Tools = CreateAltaTools(
                backendId,
                sourceThreadIdProvider: sourceThreadIdProvider,
                sourceProjectIdProvider: () => sourceProjectId,
                workingDirectoryProvider: () => workingDirectory),
            OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
            OnUserInputRequest = (request, cancellationToken) => _userInputRequests.HandleAsync(CreateTransientThreadKey(backendId, workingDirectory), request, cancellationToken),
        };
    }

    public WorkThreadExecutionOptions BuildExecutionOptions(WorkThreadDescriptor thread, OpenThreadState tab)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(tab);

        var workingDirectory = ResolveWorkingDirectory(thread);
        var projectRoots = ResolveProjectRoots(thread);
        var backendId = new AgentBackendId(thread.BackendId);
        return new WorkThreadExecutionOptions
        {
            BackendId = backendId,
            ProviderKey = thread.ResolvedProviderKey,
            WorkingDirectory = workingDirectory,
            ProjectRoots = projectRoots,
            Model = tab.ModelId,
            ReasoningEffort = tab.ReasoningEffort,
            Tools = CreateAltaTools(
                backendId,
                sourceThreadIdProvider: () => thread.ThreadId,
                sourceProjectIdProvider: () => thread.ProjectRef,
                workingDirectoryProvider: () => ResolveWorkingDirectory(thread)),
            OnPermissionRequest = CreatePermissionHandler(backendId, thread.ThreadId),
            OnUserInputRequest = (request, cancellationToken) => _userInputRequests.HandleAsync(thread.ThreadId, request, cancellationToken),
        };
    }

    public static string CreateTransientThreadKey(AgentBackendId backendId, string workingDirectory)
        => $"{backendId.Value}:{workingDirectory}";

    private AgentPermissionRequestHandler CreatePermissionHandler(AgentBackendId backendId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        return string.Equals(backendId.Value, AgentBackendIds.Codex.Value, StringComparison.OrdinalIgnoreCase)
            ? static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce))
            : (request, cancellationToken) => _permissionRequests.HandleAsync(threadId, request, cancellationToken);
    }

    private IReadOnlyList<AgentToolDefinition>? CreateAltaTools(
        AgentBackendId backendId,
        Func<string?>? sourceThreadIdProvider,
        Func<string?>? sourceProjectIdProvider,
        Func<string?>? workingDirectoryProvider)
    {
        if (_altaServices is null || !_altaToolBackendIds.Contains(backendId.Value))
        {
            return null;
        }

        var dispatcher = _altaServices.GetService(typeof(AltaCommandDispatcher)) as AltaCommandDispatcher
            ?? new AltaCommandDispatcher(new AltaCommandRegistry(), _altaServices);
        return
        [
            AltaSessionToolFactory.Create(
                dispatcher,
                new AltaSessionToolOptions
                {
                    SourceThreadIdProvider = sourceThreadIdProvider,
                    SourceProjectIdProvider = sourceProjectIdProvider,
                    WorkingDirectoryProvider = workingDirectoryProvider,
                    DefaultMaxOutputRecords = 200,
                    DefaultMaxOutputBytes = 64 * 1024,
                    DefaultTimeout = TimeSpan.FromSeconds(120),
                }),
        ];
    }

    private string ResolveWorkingDirectory(WorkThreadDescriptor thread)
    {
        return thread.Kind switch
        {
            WorkThreadKind.GlobalThread => _catalogOptions.GlobalRoot,
            WorkThreadKind.ProjectThread when _threadSelection.GetProjectById(thread.ProjectRef) is { } project => project.ProjectPath,
            _ => thread.WorkingDirectory,
        };
    }

    private IReadOnlyList<string> ResolveProjectRoots(WorkThreadDescriptor thread)
    {
        if (_threadSelection.GetProjectById(thread.ProjectRef) is { } project)
        {
            return [project.ProjectPath];
        }

        return [];
    }
}
