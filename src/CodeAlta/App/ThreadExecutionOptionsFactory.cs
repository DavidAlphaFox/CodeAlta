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
    private readonly Dictionary<string, ModelProviderState> _modelProviderStates;
    private readonly ThreadSelectionContext _threadSelection;
    private readonly ThreadPermissionRequestCoordinator _permissionRequests;
    private readonly ThreadUserInputRequestCoordinator _userInputRequests;
    private readonly IServiceProvider? _altaServices;
    private readonly IReadOnlySet<string> _altaToolProviderIds;

    public ThreadExecutionOptionsFactory(
        CatalogOptions catalogOptions,
        Dictionary<string, ModelProviderState> modelProviderStates,
        ThreadSelectionContext threadSelection,
        ThreadPermissionRequestCoordinator permissionRequests,
        ThreadUserInputRequestCoordinator userInputRequests,
        IServiceProvider? altaServices = null,
        IReadOnlySet<string>? altaToolProviderIds = null)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(modelProviderStates);
        ArgumentNullException.ThrowIfNull(threadSelection);
        ArgumentNullException.ThrowIfNull(permissionRequests);
        ArgumentNullException.ThrowIfNull(userInputRequests);

        _catalogOptions = catalogOptions;
        _modelProviderStates = modelProviderStates;
        _threadSelection = threadSelection;
        _permissionRequests = permissionRequests;
        _userInputRequests = userInputRequests;
        _altaServices = altaServices;
        _altaToolProviderIds = altaToolProviderIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public SessionExecutionOptions BuildPreferredExecutionOptions(
        ModelProviderId providerId,
        string workingDirectory,
        IReadOnlyList<string> projectRoots,
        Func<string?>? sourceThreadIdProvider = null)
    {
        ArgumentNullException.ThrowIfNull(projectRoots);

        _modelProviderStates.TryGetValue(providerId.Value, out var backendState);
        var model = backendState?.SelectedModelId;
        var reasoning = backendState?.SelectedReasoningEffort;

        var sourceProjectId = projectRoots.Count == 0
            ? null
            : _threadSelection.GetSelectedProjectId();
        return new SessionExecutionOptions
        {
            ProviderId = providerId,
            ProviderKey = providerId.Value,
            WorkingDirectory = workingDirectory,
            ProjectRoots = projectRoots,
            Model = model,
            ReasoningEffort = reasoning,
            Tools = CreateAltaTools(
                providerId,
                sourceThreadIdProvider: sourceThreadIdProvider,
                sourceProjectIdProvider: () => sourceProjectId,
                workingDirectoryProvider: () => workingDirectory),
            OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
            OnUserInputRequest = (request, cancellationToken) => _userInputRequests.HandleAsync(CreateTransientThreadKey(providerId, workingDirectory), request, cancellationToken),
        };
    }

    public SessionExecutionOptions BuildExecutionOptions(SessionViewDescriptor thread, OpenThreadState tab)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(tab);

        var workingDirectory = ResolveWorkingDirectory(thread);
        var projectRoots = ResolveProjectRoots(thread);
        var providerId = new ModelProviderId(thread.ResolvedProviderKey);
        return new SessionExecutionOptions
        {
            ProviderId = providerId,
            ProviderKey = thread.ResolvedProviderKey,
            WorkingDirectory = workingDirectory,
            ProjectRoots = projectRoots,
            Model = tab.ModelId,
            ReasoningEffort = tab.ReasoningEffort,
            Tools = CreateAltaTools(
                providerId,
                sourceThreadIdProvider: () => thread.ThreadId,
                sourceProjectIdProvider: () => thread.ProjectRef,
                workingDirectoryProvider: () => ResolveWorkingDirectory(thread)),
            OnPermissionRequest = CreatePermissionHandler(providerId, thread.ThreadId),
            OnUserInputRequest = (request, cancellationToken) => _userInputRequests.HandleAsync(thread.ThreadId, request, cancellationToken),
        };
    }

    public static string CreateTransientThreadKey(ModelProviderId providerId, string workingDirectory)
        => $"{providerId.Value}:{workingDirectory}";

    private AgentPermissionRequestHandler CreatePermissionHandler(ModelProviderId providerId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        return string.Equals(providerId.Value, ModelProviderIds.Codex.Value, StringComparison.OrdinalIgnoreCase)
            ? static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce))
            : (request, cancellationToken) => _permissionRequests.HandleAsync(threadId, request, cancellationToken);
    }

    private IReadOnlyList<AgentToolDefinition>? CreateAltaTools(
        ModelProviderId providerId,
        Func<string?>? sourceThreadIdProvider,
        Func<string?>? sourceProjectIdProvider,
        Func<string?>? workingDirectoryProvider)
    {
        if (_altaServices is null || !_altaToolProviderIds.Contains(providerId.Value))
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

    private string ResolveWorkingDirectory(SessionViewDescriptor thread)
    {
        return thread.Kind switch
        {
            WorkThreadKind.GlobalThread => _catalogOptions.GlobalRoot,
            WorkThreadKind.ProjectThread when _threadSelection.GetProjectById(thread.ProjectRef) is { } project => project.ProjectPath,
            _ => thread.WorkingDirectory,
        };
    }

    private IReadOnlyList<string> ResolveProjectRoots(SessionViewDescriptor thread)
    {
        if (_threadSelection.GetProjectById(thread.ProjectRef) is { } project)
        {
            return [project.ProjectPath];
        }

        return [];
    }
}
